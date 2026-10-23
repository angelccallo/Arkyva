SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO
IF OBJECT_ID(N'arkyva.Files', N'U') IS NULL
BEGIN
    CREATE TABLE arkyva.Files
    (
        FileId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_arkyva_Files PRIMARY KEY,
        OriginalName NVARCHAR(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Extension NVARCHAR(30) COLLATE Latin1_General_100_BIN2 NULL,
        MimeType NVARCHAR(255) NULL,
        SizeBytes BIGINT NOT NULL,
        ProviderId NVARCHAR(100) COLLATE Latin1_General_100_BIN2 NOT NULL,
        StorageKey NVARCHAR(1000) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Status VARCHAR(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CreatedUtc DATETIME2(7) NOT NULL CONSTRAINT DF_arkyva_Files_CreatedUtc DEFAULT SYSUTCDATETIME(),
        UpdatedUtc DATETIME2(7) NULL,
        DeletedUtc DATETIME2(7) NULL,
        StorageIdentityHash AS CONVERT(BINARY(32), HASHBYTES('SHA2_256',
            CONVERT(BINARY(4), DATALENGTH(ProviderId)) + CONVERT(VARBINARY(200), ProviderId) +
            CONVERT(VARBINARY(2000), StorageKey))) PERSISTED,
        CONSTRAINT CK_arkyva_Files_Id CHECK (FileId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_arkyva_Files_Size CHECK (SizeBytes >= 0),
        CONSTRAINT CK_arkyva_Files_Name CHECK (LEN(LTRIM(RTRIM(OriginalName))) > 0),
        CONSTRAINT CK_arkyva_Files_Provider CHECK (LEN(LTRIM(RTRIM(ProviderId))) > 0),
        CONSTRAINT CK_arkyva_Files_Key CHECK (LEN(LTRIM(RTRIM(StorageKey))) > 0),
        CONSTRAINT CK_arkyva_Files_Status CHECK (Status IN ('Pending', 'Active', 'Deleted', 'Failed') AND
            DATALENGTH(Status) = CASE WHEN Status IN ('Pending', 'Deleted') THEN 7 ELSE 6 END),
        CONSTRAINT CK_arkyva_Files_Deletion CHECK (
            (Status = 'Deleted' AND DeletedUtc IS NOT NULL AND DeletedUtc >= CreatedUtc) OR
            (Status <> 'Deleted' AND DeletedUtc IS NULL))
    );
END;
GO
