# DataTransferUtility

A configurable .NET console application for seamless data transfer between databases and file systems with SFTP capabilities.

## Overview

DataTransferUtility is a flexible and configurable console application that simplifies the process of transferring data between databases and external systems. It currently supports exporting data from SQL databases to CSV files and uploading them via SFTP, with plans to add import functionality in the future.

## Features

- **Database to CSV Export**: Query data from SQL Server and export to CSV files
- **Data Grouping**: Group and split data exports based on configurable field values
- **Secure File Transfer**: Upload generated files to remote servers via SFTP
- **Highly Configurable**: All settings are managed through configuration without code changes
- **Error Handling**: Comprehensive error logging and exception management
- **File Management**: Organized directory structure with tracking of processed files

## Getting Started

### Prerequisites

- .NET Framework 4.7.2+ or .NET Core 3.1+
- SQL Server database (for data source)
- SFTP server details (if using file transfer capabilities)

### Installation

1. Clone the repository:
   ```
   git clone https://github.com/yourusername/DataTransferUtility.git
   ```

2. Set up the configuration file:
   - Modify the `appsettings.json` with your database connection string, SFTP details, and export settings
   - See the example configuration section below

3. Build the application:
   ```
   dotnet build
   ```

### Example Configuration

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=yourserver;Database=YourDatabase;Trusted_Connection=True;"
  },
  "DataSource": {
    "TableName": "Schema.YourTable"
  },
  "Directories": {
    "ExportPath": "C:\\Exports\\Data"
  },
  "PrepareData": true,
  "StoredProcedures": {
    "DataPreparation": "Schema.usp_PrepareExportData"
  },
  "DataExport": {
    "GroupByField": "RecordIdentifier",
    "GroupByLength": "4",
    "FileNameTemplate": "DataExport_{0}.csv",
    "MultiGroupFileNameTemplate": "DataExport_{0}_Group{1}.csv"
  },
  "SftpConfig": {
    "DisableSftp": false,
    "HostName": "sftp.example.com",
    "UserName": "username",
    "Password": "password",
    "SshHostKeyFingerprint": "ssh-rsa-xxxxxxxxxxx",
    "SshKeyPath": "C:\\Keys\\id_rsa.ppk",
    "SshKeyPassphrase": "",
    "RemoteDirectory": "/uploads/data"
  }
}
```

## Usage

Run the application to export data and transfer files:

```
dotnet run
```

The application will:
1. Connect to the specified database
2. Run any preparation stored procedures if configured
3. Fetch data from the specified table
4. Group the data based on the configured field
5. Export each group to a CSV file
6. Upload the files via SFTP (if enabled)
7. Move processed files to the "Sent" directory

## Roadmap

- [ ] Add import functionality to support data ingestion
- [ ] Support for multiple file formats (JSON, XML, etc.)
- [ ] Add scheduling capabilities
- [ ] Implement data transformation options
- [ ] Add support for cloud storage providers (Azure Blob, S3, etc.)

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Acknowledgements

- [WinSCP](https://winscp.net/) for SFTP functionality
- [CsvHelper](https://joshclose.github.io/CsvHelper/) for CSV file handling
- [Microsoft.Extensions.Configuration](https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.configuration) for configuration management