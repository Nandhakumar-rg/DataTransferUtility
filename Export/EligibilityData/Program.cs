using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using CsvHelper;
using WinSCP;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Data;
using System.Data.OleDb;

namespace DataExporter
{
    class Program
    {
        static void Main()
        {
            /*
             * This console application creates CSV files from database data and uploads them via SFTP.
             * It supports grouping data by specific fields and handling multiple output files.
             */

            // Load configuration
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            string connectionString = config.GetConnectionString("DefaultConnection");
            string tableName = config["DataSource:TableName"];
            string sftpRemoteDirectory = config["SftpConfig:RemoteDirectory"];
            string currentDate = DateTime.Now.ToString("yyyyMMdd"); // Format YYYYMMDD

            // Define export and sent directories
            string exportDirectory = config["Directories:ExportPath"];
            string sentDirectory = Path.Combine(exportDirectory, "Sent");

            // Ensure directories exist
            if (!Directory.Exists(exportDirectory))
            {
                Directory.CreateDirectory(exportDirectory);
                Console.WriteLine($" Created directory: {exportDirectory}");
            }

            if (!Directory.Exists(sentDirectory))
            {
                Directory.CreateDirectory(sentDirectory);
                Console.WriteLine($" Created directory: {sentDirectory}");
            }

            try
            {
                // Prepare data if needed
                bool prepareData = false;
                if (bool.TryParse(config["PrepareData"], out prepareData) && prepareData)
                {
                    PrepareData(connectionString, config["StoredProcedures:DataPreparation"]);
                }

                Console.WriteLine(" Fetching data from database...");
                List<Dictionary<string, object>> data = FetchData(connectionString, tableName);

                if (data.Count == 0)
                {
                    Console.WriteLine(" No data found in the table!");
                    return;
                }

                // Get the field to group by from config
                string groupByField = config["DataExport:GroupByField"];
                string groupByLength = config["DataExport:GroupByLength"];

                int groupByFieldLength = !string.IsNullOrEmpty(groupByLength) ?
                    int.Parse(groupByLength) : 4;

                Console.WriteLine($" Splitting data by {groupByField}...");
                var groupedData = SplitDataByField(data, groupByField, groupByFieldLength);

                // File name template from config
                string fileNameTemplate = config["DataExport:FileNameTemplate"];

                // Check if there's only one group
                if (groupedData.Keys.Count == 1)
                {
                    // Single group case
                    string groupValue = groupedData.Keys.First();
                    string fileName = string.Format(fileNameTemplate, currentDate);
                    string filePath = Path.Combine(exportDirectory, fileName);

                    Console.WriteLine($" Exporting data to CSV for {groupByField} {groupValue}...");
                    ExportToCsv(groupedData[groupValue], filePath);

                    Console.WriteLine($" Uploading {fileName} via SFTP...");
                    bool sftpSuccess = PerformSftpTransfer(filePath, sftpRemoteDirectory);

                    if (sftpSuccess)
                    {
                        Console.WriteLine($" {fileName} uploaded successfully!");

                        // Move file to Sent directory after successful upload
                        string destinationPath = Path.Combine(sentDirectory, fileName);
                        File.Move(filePath, destinationPath, true);
                        Console.WriteLine($" Moved {fileName} to {sentDirectory}");
                    }
                    else
                    {
                        Console.WriteLine($" Failed to upload {fileName}!");
                    }
                }
                else
                {
                    // Multiple groups case
                    string multiGroupTemplate = config["DataExport:MultiGroupFileNameTemplate"];

                    foreach (var groupValue in groupedData.Keys)
                    {
                        // Create a unique file name with group designation
                        string fileName = string.Format(multiGroupTemplate, currentDate, groupValue);
                        string filePath = Path.Combine(exportDirectory, fileName);

                        Console.WriteLine($" Exporting data to CSV for {groupByField} {groupValue}...");
                        ExportToCsv(groupedData[groupValue], filePath);

                        Console.WriteLine($" Uploading {fileName} via SFTP...");
                        bool sftpSuccess = PerformSftpTransfer(filePath, sftpRemoteDirectory);

                        if (sftpSuccess)
                        {
                            Console.WriteLine($" {fileName} uploaded successfully!");

                            // Move file to Sent directory after successful upload
                            string destinationPath = Path.Combine(sentDirectory, fileName);
                            File.Move(filePath, destinationPath, true);
                            Console.WriteLine($" Moved {fileName} to {sentDirectory}");
                        }
                        else
                        {
                            Console.WriteLine($" Failed to upload {fileName}!");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Error: {ex.Message}");
                Console.WriteLine($" Stack trace: {ex.StackTrace}");
            }
        }

        static void PrepareData(string connectionString, string storedProcedureName)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                // Create the command and set its properties
                SqlCommand command = new SqlCommand();
                command.Connection = connection;
                command.CommandText = storedProcedureName;
                command.CommandType = CommandType.StoredProcedure;

                // Open the connection and execute the command
                connection.Open();
                command.ExecuteNonQuery();
                Console.WriteLine(" Data preparation complete");
            }
        }

        static void ExecuteStoredProcedure(string connectionString, string procedureName, Dictionary<string, object> parameters)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                // Create the command and set its properties
                SqlCommand command = new SqlCommand();
                command.Connection = connection;
                command.CommandText = procedureName;
                command.CommandType = CommandType.StoredProcedure;

                // Add parameters
                foreach (var param in parameters)
                {
                    command.Parameters.AddWithValue(param.Key, param.Value);
                }

                // Open the connection and execute the command
                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        // Fetch Data from SQL Server
        static List<Dictionary<string, object>> FetchData(string connectionString, string tableName)
        {
            var dataList = new List<Dictionary<string, object>>();

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                string query = $"SELECT * FROM {tableName}";
                SqlCommand command = new SqlCommand(query, conn);

                conn.Open();

                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var row = new Dictionary<string, object>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[reader.GetName(i)] = reader[i];
                        }
                        dataList.Add(row);
                    }
                }
            }
            return dataList;
        }

        // Split Data by a specific field
        static Dictionary<string, List<Dictionary<string, object>>> SplitDataByField(
            List<Dictionary<string, object>> data, string fieldName, int prefixLength)
        {
            var groupedData = new Dictionary<string, List<Dictionary<string, object>>>();

            foreach (var row in data)
            {
                string fieldValue = row[fieldName]?.ToString() ?? "";
                string groupKey = fieldValue;

                if (fieldValue.Length >= prefixLength)
                {
                    groupKey = fieldValue.Substring(0, prefixLength);
                }

                if (!groupedData.ContainsKey(groupKey))
                {
                    groupedData[groupKey] = new List<Dictionary<string, object>>();
                }
                groupedData[groupKey].Add(row);
            }
            return groupedData;
        }

        // Export Data to CSV
        static void ExportToCsv(List<Dictionary<string, object>> data, string filePath)
        {
            using (var writer = new StreamWriter(filePath))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                if (data.Count > 0)
                {
                    foreach (var key in data[0].Keys)
                    {
                        csv.WriteField(key);
                    }
                    csv.NextRecord();

                    foreach (var row in data)
                    {
                        foreach (var value in row.Values)
                        {
                            csv.WriteField(value?.ToString() ?? "");
                        }
                        csv.NextRecord();
                    }
                }
            }
        }

        // Perform SFTP Transfer using WinSCP
        static bool PerformSftpTransfer(string localFilePath, string remoteDirectory)
        {
            // For development testing, you can disable actual SFTP transfer
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            bool disableSftp = false;
            if (bool.TryParse(config["SftpConfig:DisableSftp"], out disableSftp) && disableSftp)
            {
                Console.WriteLine(" SFTP transfer disabled in configuration. Simulating successful upload.");
                return true;
            }

            // Load SFTP Configuration
            string hostName = config["SftpConfig:HostName"];
            string userName = config["SftpConfig:UserName"];
            string password = config["SftpConfig:Password"];
            string sshKeyFingerprint = config["SftpConfig:SshHostKeyFingerprint"];

            if (string.IsNullOrWhiteSpace(hostName) || string.IsNullOrWhiteSpace(userName) ||
                (string.IsNullOrWhiteSpace(password) && string.IsNullOrWhiteSpace(config["SftpConfig:SshKeyPath"])))
            {
                Console.WriteLine(" Error: Missing SFTP credentials in appsettings.json");
                return false;
            }

            string remoteFilePath = remoteDirectory.TrimEnd('/') + "/" + Path.GetFileName(localFilePath);

            SessionOptions sessionOptions = new SessionOptions
            {
                Protocol = Protocol.Sftp,
                HostName = hostName,
                UserName = userName,
                Password = password,
                SshHostKeyFingerprint = sshKeyFingerprint
            };

            // Use SSH key if specified
            if (!string.IsNullOrWhiteSpace(config["SftpConfig:SshKeyPath"]))
            {
                sessionOptions.SshPrivateKeyPath = config["SftpConfig:SshKeyPath"];
                if (!string.IsNullOrWhiteSpace(config["SftpConfig:SshKeyPassphrase"]))
                {
                    sessionOptions.PrivateKeyPassphrase = config["SftpConfig:SshKeyPassphrase"];
                }
            }

            try
            {
                using (Session session = new Session())
                {
                    session.Open(sessionOptions);
                    Console.WriteLine(" Connected to SFTP server!");

                    TransferOptions transferOptions = new TransferOptions
                    {
                        TransferMode = TransferMode.Binary,
                        OverwriteMode = OverwriteMode.Overwrite
                    };

                    Console.WriteLine($" Uploading {localFilePath} to {remoteFilePath}...");
                    TransferOperationResult transferResult = session.PutFiles(localFilePath, remoteFilePath, false, transferOptions);
                    transferResult.Check();

                    Console.WriteLine(" File uploaded successfully!");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" SFTP Error: {ex.Message}");
                return false;
            }
        }
    }
}