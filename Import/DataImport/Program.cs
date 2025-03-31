using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using WinSCP;
using Microsoft.Extensions.Configuration;

namespace DataTransferUtility.Import
{
    public class ImportProcessor
    {
        static void Main()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            Console.WriteLine(" Starting SFTP download and data processing...");

            // Load configuration
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            string connectionString = config.GetConnectionString("DefaultConnection");
            string remoteDirectory = config["SftpConfig:RemoteDirectory"];
            string localDirectory = config["ImportConfig:LocalDirectory"];
            string fileNamePattern = config["ImportConfig:FileNamePattern"];
            bool cleanupFiles = bool.TryParse(config["ImportConfig:CleanupFiles"], out bool result) && result;

            Directory.CreateDirectory(localDirectory); // Ensure local folder exists

            try
            {
                Console.WriteLine(" Downloading Excel files from SFTP...");
                List<string> downloadedFiles = DownloadFilesFromSftp(remoteDirectory, localDirectory, fileNamePattern);

                if (downloadedFiles.Count == 0)
                {
                    Console.WriteLine(" No files downloaded. Exiting...");
                    return;
                }

                Console.WriteLine(" Files downloaded successfully!");

                foreach (var file in downloadedFiles)
                {
                    Console.WriteLine($" Processing file: {file}");

                    // Extract identifiers from filename using configured pattern
                    var identifiers = ExtractIdentifiers(file, config["ImportConfig:FileNamePattern"]);

                    if (identifiers.Count == 0)
                    {
                        Console.WriteLine($" Skipping file {file} (Invalid format)");
                        continue;
                    }

                    // Delete existing records based on extracted identifiers
                    DeleteExistingRecords(connectionString,
                                        config["ImportConfig:TargetTable"],
                                        identifiers,
                                        config["ImportConfig:DeleteCondition"]);

                    // Process and merge data from all sheets
                    List<Dictionary<string, object>> processedData = ProcessFile(file, identifiers);

                    // Insert the new data
                    InsertDataIntoDatabase(connectionString,
                                          config["ImportConfig:TargetTable"],
                                          processedData,
                                          config["ImportConfig:ColumnMappings"]);

                    Console.WriteLine($" Finished processing {file}");
                }

                Console.WriteLine(" Process completed successfully!");

                // Clean up local files if configured
                if (cleanupFiles)
                {
                    DeleteAllLocalFiles(localDirectory);
                    Console.WriteLine(" Local directory cleaned up!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Error: {ex.Message}");
                Console.WriteLine($" Stack trace: {ex.StackTrace}");
            }
        }

        // Download Excel files from SFTP
        public static List<string> DownloadFilesFromSftp(string remoteDirectory, string localDirectory, string filePattern)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            string hostName = config["SftpConfig:HostName"];
            string userName = config["SftpConfig:UserName"];
            string password = config["SftpConfig:Password"];
            string sshKeyFingerprint = config["SftpConfig:SshHostKeyFingerprint"];
            string sshKeyPath = config["SftpConfig:SshKeyPath"];

            List<string> downloadedFiles = new List<string>();

            SessionOptions sessionOptions = new SessionOptions
            {
                Protocol = Protocol.Sftp,
                HostName = hostName,
                UserName = userName,
                Password = password,
                SshHostKeyFingerprint = sshKeyFingerprint
            };

            // Use SSH key if specified
            if (!string.IsNullOrWhiteSpace(sshKeyPath))
            {
                sessionOptions.SshPrivateKeyPath = sshKeyPath;
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

                    RemoteDirectoryInfo directoryInfo = session.ListDirectory(remoteDirectory);
                    foreach (RemoteFileInfo fileInfo in directoryInfo.Files)
                    {
                        if (!fileInfo.IsDirectory && MatchesFilePattern(fileInfo.Name, filePattern))
                        {
                            string remoteFilePath = Path.Combine(remoteDirectory, fileInfo.Name)
                                                   .Replace('\\', '/');
                            string localFilePath = Path.Combine(localDirectory, fileInfo.Name);

                            Console.WriteLine($" Downloading {remoteFilePath}...");
                            TransferOperationResult transferResult = session.GetFiles(remoteFilePath, localFilePath);
                            transferResult.Check();

                            if (File.Exists(localFilePath))
                            {
                                Console.WriteLine($" Downloaded: {fileInfo.Name}");
                                downloadedFiles.Add(localFilePath);
                            }
                            else
                            {
                                Console.WriteLine($" Failed to download: {fileInfo.Name}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" SFTP Error: {ex.Message}");
            }

            return downloadedFiles;
        }

        // Check if filename matches the configured pattern
        private static bool MatchesFilePattern(string fileName, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                       fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase);
            }

            // Create a regex pattern from the configured pattern
            // (simplified implementation that supports basic wildcards)
            string regexPattern = "^" + pattern
                .Replace(".", "\\.")
                .Replace("*", ".*")
                .Replace("?", ".") + "$";

            return System.Text.RegularExpressions.Regex.IsMatch(
                fileName,
                regexPattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
        }

        // Extract identifiers from filename using the provided pattern
        public static Dictionary<string, string> ExtractIdentifiers(string filePath, string pattern)
        {
            var identifiers = new Dictionary<string, string>();
            string fileName = Path.GetFileNameWithoutExtension(filePath);

            // Parse the pattern string to extract field names and their positions
            if (!string.IsNullOrEmpty(pattern))
            {
                var patternParts = pattern.Split(new[] { '-' });
                var nameParts = fileName.Split(new[] { '-' });

                // Parse custom pattern (simplified logic)
                // Actual implementation would need more robust parsing of the pattern
                try
                {
                    for (int i = 0; i < patternParts.Length && i < nameParts.Length; i++)
                    {
                        if (patternParts[i].StartsWith("{") && patternParts[i].EndsWith("}"))
                        {
                            string key = patternParts[i].Trim('{', '}');
                            string value = nameParts[i].Trim();
                            identifiers[key] = value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($" Error parsing filename: {ex.Message}");
                }
            }
            else
            {
                // Default parsing logic if no pattern provided
                string[] parts = fileName.Split('-');
                if (parts.Length >= 3)
                {
                    identifiers["PrimaryId"] = parts[0];
                    identifiers["SecondaryId"] = parts.Length > 1 ? parts[1] : "";
                    identifiers["TertiaryId"] = parts.Length > 2 ? parts[2] : "";
                }
            }

            return identifiers;
        }

        // Process Excel file and merge all sheet data
        public static List<Dictionary<string, object>> ProcessFile(string filePath, Dictionary<string, string> identifiers)
        {
            List<Dictionary<string, object>> processedData = new List<Dictionary<string, object>>();

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                foreach (var worksheet in package.Workbook.Worksheets)
                {
                    if (worksheet.Dimension == null) continue;

                    int rowCount = worksheet.Dimension.Rows;
                    int colCount = worksheet.Dimension.Columns;

                    // Extract headers
                    List<string> headers = new List<string>();
                    for (int col = 1; col <= colCount; col++)
                    {
                        string header = worksheet.Cells[1, col].Text.Trim();
                        headers.Add(string.IsNullOrEmpty(header) ? $"Column{col}" : header);
                    }

                    // Process data rows
                    for (int row = 2; row <= rowCount; row++)
                    {
                        Dictionary<string, object> rowData = new Dictionary<string, object>();

                        // Add extracted identifiers to each row
                        foreach (var identifier in identifiers)
                        {
                            rowData[identifier.Key] = identifier.Value;
                        }

                        // Add cell values
                        for (int col = 1; col <= colCount; col++)
                        {
                            if (col <= headers.Count)
                            {
                                rowData[headers[col - 1]] = worksheet.Cells[row, col].Text;
                            }
                        }

                        processedData.Add(rowData);
                    }
                }
            }

            return processedData;
        }

        // Delete existing records based on provided identifiers
        public static void DeleteExistingRecords(string connectionString, string tableName, Dictionary<string, string> identifiers, string deleteCondition)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();

                string deleteQuery = $"DELETE FROM {tableName} WHERE ";

                // If a specific delete condition is provided, use it with parameters
                if (!string.IsNullOrEmpty(deleteCondition))
                {
                    deleteQuery += deleteCondition;
                }
                else
                {
                    // Otherwise build a simple condition from the identifiers
                    var conditions = new List<string>();
                    foreach (var identifier in identifiers)
                    {
                        conditions.Add($"{identifier.Key} = @{identifier.Key}");
                    }
                    deleteQuery += string.Join(" AND ", conditions);
                }

                using (SqlCommand cmd = new SqlCommand(deleteQuery, conn))
                {
                    foreach (var identifier in identifiers)
                    {
                        cmd.Parameters.AddWithValue($"@{identifier.Key}", identifier.Value);
                    }

                    int rowsDeleted = cmd.ExecuteNonQuery();
                    Console.WriteLine($" Deleted {rowsDeleted} rows based on conditions.");
                }
            }
        }

        // Insert processed data into the database
        public static void InsertDataIntoDatabase(
            string connectionString,
            string tableName,
            List<Dictionary<string, object>> data,
            string columnMappingString)
        {
            // Parse column mappings from configuration
            var columnMappings = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(columnMappingString))
            {
                foreach (var mapping in columnMappingString.Split(','))
                {
                    var parts = mapping.Split('=');
                    if (parts.Length == 2)
                    {
                        columnMappings[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();

                foreach (var row in data)
                {
                    // Create parameter list and column list for this row
                    var columns = new List<string>();
                    var parameters = new List<string>();

                    foreach (var item in row)
                    {
                        string columnName = item.Key;

                        // Apply column mapping if exists
                        if (columnMappings.ContainsKey(columnName))
                        {
                            columnName = columnMappings[columnName];
                        }

                        columns.Add(columnName);
                        parameters.Add($"@{columnName.Replace(" ", "_")}");
                    }

                    // Add CreatedAt timestamp
                    columns.Add("CreatedAt");
                    parameters.Add("@CreatedAt");

                    string insertQuery = $"INSERT INTO {tableName} ([{string.Join("], [", columns)}]) VALUES ({string.Join(", ", parameters)})";

                    using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
                    {
                        foreach (var item in row)
                        {
                            string columnName = item.Key;
                            if (columnMappings.ContainsKey(columnName))
                            {
                                columnName = columnMappings[columnName];
                            }

                            cmd.Parameters.AddWithValue($"@{columnName.Replace(" ", "_")}", item.Value ?? DBNull.Value);
                        }

                        // Set timestamp
                        cmd.Parameters.AddWithValue("@CreatedAt", DateTime.Now);

                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        // Delete all files in the local directory
        public static void DeleteAllLocalFiles(string localDirectory)
        {
            foreach (var file in Directory.GetFiles(localDirectory))
            {
                File.Delete(file);
            }
        }

    }
}