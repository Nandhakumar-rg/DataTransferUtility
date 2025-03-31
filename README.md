# DataTransferUtility

A configurable .NET console application for seamless data transfer between databases and file systems with SFTP capabilities.

## Overview

DataTransferUtility is a flexible and configurable console application that simplifies the process of transferring data between databases and external systems. It supports both:
- **Export**: Query data from SQL databases, export to CSV files, and upload via SFTP
- **Import**: Download Excel files from SFTP, process their contents, and import into SQL databases

## Features

### Shared Features
- **Secure File Transfer**: Transfer files securely via SFTP
- **Highly Configurable**: All settings are managed through configuration without code changes
- **Error Handling**: Comprehensive error logging and exception management

### Export Module
- **Database to CSV Export**: Query data from SQL Server and export to CSV files
- **Data Grouping**: Group and split data exports based on configurable field values
- **File Management**: Organized directory structure with tracking of processed files

### Import Module
- **Excel File Processing**: Download and process Excel files from SFTP servers
- **Multi-sheet Support**: Process and merge data from all sheets in workbooks
- **Pattern Matching**: Identify and extract metadata from filenames using patterns
- **Smart Data Mapping**: Map Excel columns to database columns using configuration

## Getting Started

### Prerequisites

- .NET Framework 4.7.2+ or .NET Core 3.1+
- SQL Server database (for data source/target)
- SFTP server details (for file transfer)
- EPPlus library (for Excel processing)

### Installation

1. Clone the repository:
   ```
   git clone https://github.com/yourusername/DataTransferUtility.git
   ```

2. Set up the configuration file:
   - Modify the `appsettings.json` with your database connection string, SFTP details, and import/export settings
   - See the example configurations below

3. Build the application:
   ```
   dotnet build
   ```

### Example Export Configuration

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

### Example Import Configuration

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=YourDatabase;Trusted_Connection=True;"
  },
  "SftpConfig": {
    "HostName": "sftp.example.com",
    "UserName": "username",
    "Password": "password",
    "SshHostKeyFingerprint": "ssh-rsa-xxxxxxxxxxx",
    "SshKeyPath": "C:\\Keys\\id_rsa.ppk",
    "SshKeyPassphrase": "",
    "RemoteDirectory": "/downloads/data"
  },
  "ImportConfig": {
    "LocalDirectory": "C:\\Imports\\Data",
    "FileNamePattern": "{Year}-{Code}-{ID}*.xlsx",
    "TargetTable": "dbo.ImportedData",
    "DeleteCondition": "PrimaryId = @PrimaryId AND SecondaryId = @SecondaryId",
    "ColumnMappings": "SI ID=StudentId, Eff Dt=EffectiveDate, Exp Dt=ExpirationDate",
    "CleanupFiles": true
  }
}
```

## Usage

### Export Module

Run the export module to extract data from a database and transfer files:

```
dotnet run --project DataTransferUtility.Export
```

The export module will:
1. Connect to the specified database
2. Run any preparation stored procedures if configured
3. Fetch data from the specified table
4. Group the data based on the configured field
5. Export each group to a CSV file
6. Upload the files via SFTP (if enabled)
7. Move processed files to the "Sent" directory

### Import Module

Run the import module to download and process Excel files:

```
dotnet run --project DataTransferUtility.Import
```

The import module will:
1. Connect to the SFTP server
2. Download Excel files matching the specified pattern
3. Extract identifiers from filenames
4. Delete existing records from the database if needed
5. Process all sheets in the Excel files
6. Insert the data into the database
7. Clean up local files (if configured)

## Roadmap

- [x] Export functionality
- [x] Import functionality
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
- [EPPlus](https://github.com/EPPlusSoftware/EPPlus) for Excel processing
- [Microsoft.Extensions.Configuration](https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.configuration) for configuration management