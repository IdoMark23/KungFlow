IF DB_ID(N'KungFlowDB') IS NULL
BEGIN
    CREATE DATABASE KungFlowDB;
END;
GO

USE KungFlowDB;
GO

IF OBJECT_ID(N'dbo.MetricsSamples', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MetricsSamples (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId NVARCHAR(64) NOT NULL,
        Platform NVARCHAR(50) NOT NULL
            CONSTRAINT CK_MetricsSamples_Platform
            CHECK (Platform IN ('extension', 'desktop', 'web', 'mobile')),
        Timestamp DATETIME2 NOT NULL,
        OpenTabsCount INT NULL,
        TabSwitchCount INT NULL,
        DeleteKeyCount INT NULL,
        KeyPressCount INT NULL,
        TypingSpeed FLOAT NULL,
        MouseSpeed FLOAT NULL,
        CreatedAt DATETIME2 NOT NULL
            CONSTRAINT DF_MetricsSamples_CreatedAt
            DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF COL_LENGTH(N'dbo.MetricsSamples', N'MouseSpeed') IS NULL
BEGIN
    ALTER TABLE dbo.MetricsSamples
    ADD MouseSpeed FLOAT NULL;
END;
GO

IF OBJECT_ID(N'dbo.Sessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Sessions (
        AccessToken NVARCHAR(255) NOT NULL PRIMARY KEY,
        UserId NVARCHAR(64) NOT NULL,
        Platform NVARCHAR(50) NOT NULL
            CONSTRAINT CK_Sessions_Platform
            CHECK (Platform IN ('extension', 'desktop', 'web', 'mobile')),
        CreatedAt DATETIME2 NOT NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users (
        Id NVARCHAR(64) NOT NULL PRIMARY KEY,
        Email NVARCHAR(255) NOT NULL UNIQUE,
        Username NVARCHAR(255) NOT NULL,
        PasswordHash NVARCHAR(255) NOT NULL,
        CreatedAt DATETIME2 NOT NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.UserCognitiveStates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserCognitiveStates (
        UserId NVARCHAR(64) NOT NULL PRIMARY KEY,
        SamplesCollected INT NOT NULL,
        Phase NVARCHAR(50) NOT NULL,
        State NVARCHAR(50) NOT NULL,
        CognitiveLoadScore FLOAT NULL,
        BaselineScore FLOAT NULL,
        ComparisonBaselineScore FLOAT NULL,
        ShouldSilenceNotifications BIT NOT NULL,
        UpdatedAt DATETIME2 NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_Sessions_Users'
)
BEGIN
    ALTER TABLE dbo.Sessions
    ADD CONSTRAINT FK_Sessions_Users
        FOREIGN KEY (UserId) REFERENCES dbo.Users(Id);
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_MetricsSamples_Users'
)
BEGIN
    ALTER TABLE dbo.MetricsSamples
    ADD CONSTRAINT FK_MetricsSamples_Users
        FOREIGN KEY (UserId) REFERENCES dbo.Users(Id);
END;
GO

CREATE OR ALTER PROCEDURE dbo.CreateUser
    @Id NVARCHAR(64),
    @Email NVARCHAR(255),
    @Username NVARCHAR(255),
    @PasswordHash NVARCHAR(255),
    @CreatedAt DATETIME2
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Users (Id, Email, Username, PasswordHash, CreatedAt)
    VALUES (@Id, LOWER(LTRIM(RTRIM(@Email))), @Username, @PasswordHash, @CreatedAt);

    SELECT Id, Email, Username, CreatedAt
    FROM dbo.Users
    WHERE Id = @Id;
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetUserByEmail
    @Email NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, Email, Username, PasswordHash, CreatedAt
    FROM dbo.Users
    WHERE Email = LOWER(LTRIM(RTRIM(@Email)));
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetUserById
    @Id NVARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, Email, Username, PasswordHash, CreatedAt
    FROM dbo.Users
    WHERE Id = @Id;
END;
GO

CREATE OR ALTER PROCEDURE dbo.UpdateUserPassword
    @Id NVARCHAR(64),
    @PasswordHash NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Users
    SET PasswordHash = @PasswordHash
    WHERE Id = @Id;

    SELECT Id, Email, Username, PasswordHash, CreatedAt
    FROM dbo.Users
    WHERE Id = @Id;
END;
GO

CREATE OR ALTER PROCEDURE dbo.CreateSession
    @AccessToken NVARCHAR(255),
    @UserId NVARCHAR(64),
    @Platform NVARCHAR(50),
    @CreatedAt DATETIME2
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Sessions (AccessToken, UserId, Platform, CreatedAt)
    VALUES (@AccessToken, @UserId, @Platform, @CreatedAt);

    SELECT AccessToken, UserId, Platform, CreatedAt
    FROM dbo.Sessions
    WHERE AccessToken = @AccessToken;
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetSessionByToken
    @AccessToken NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT AccessToken, UserId, Platform, CreatedAt
    FROM dbo.Sessions
    WHERE AccessToken = @AccessToken;
END;
GO

CREATE OR ALTER PROCEDURE dbo.DeleteSession
    @AccessToken NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.Sessions
    WHERE AccessToken = @AccessToken;

    SELECT @@ROWCOUNT AS DeletedCount;
END;
GO

CREATE OR ALTER PROCEDURE dbo.CreateMetricsSample
    @UserId NVARCHAR(64),
    @Platform NVARCHAR(50),
    @Timestamp DATETIME2,
    @OpenTabsCount INT = NULL,
    @TabSwitchCount INT = NULL,
    @DeleteKeyCount INT = NULL,
    @KeyPressCount INT = NULL,
    @TypingSpeed FLOAT = NULL,
    @MouseSpeed FLOAT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.MetricsSamples (
        UserId, Platform, Timestamp,
        OpenTabsCount, TabSwitchCount, DeleteKeyCount, KeyPressCount, TypingSpeed, MouseSpeed
    )
    VALUES (
        @UserId, @Platform, @Timestamp,
        @OpenTabsCount, @TabSwitchCount, @DeleteKeyCount, @KeyPressCount, @TypingSpeed, @MouseSpeed
    );

    SELECT
        Id, UserId, Platform, Timestamp,
        OpenTabsCount, TabSwitchCount, DeleteKeyCount, KeyPressCount, TypingSpeed, MouseSpeed,
        CreatedAt
    FROM dbo.MetricsSamples
    WHERE Id = SCOPE_IDENTITY();
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetMetricsSamplesByUserId
    @UserId NVARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id, UserId, Platform, Timestamp,
        OpenTabsCount, TabSwitchCount, DeleteKeyCount, KeyPressCount, TypingSpeed, MouseSpeed,
        CreatedAt
    FROM dbo.MetricsSamples
    WHERE UserId = @UserId
    ORDER BY Timestamp ASC;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_UserCognitiveStates_Users'
)
BEGIN
    ALTER TABLE dbo.UserCognitiveStates
    ADD CONSTRAINT FK_UserCognitiveStates_Users
        FOREIGN KEY (UserId) REFERENCES dbo.Users(Id);
END;
GO

CREATE OR ALTER PROCEDURE dbo.DeleteMetricsSamplesByUserId
    @UserId NVARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.MetricsSamples
    WHERE UserId = @UserId;

    SELECT @@ROWCOUNT AS DeletedCount;
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetUserCognitiveState
    @UserId NVARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        UserId, SamplesCollected, Phase, State,
        CognitiveLoadScore, BaselineScore, ComparisonBaselineScore,
        ShouldSilenceNotifications, UpdatedAt
    FROM dbo.UserCognitiveStates
    WHERE UserId = @UserId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.UpsertUserCognitiveState
    @UserId NVARCHAR(64),
    @SamplesCollected INT,
    @Phase NVARCHAR(50),
    @State NVARCHAR(50),
    @CognitiveLoadScore FLOAT = NULL,
    @BaselineScore FLOAT = NULL,
    @ComparisonBaselineScore FLOAT = NULL,
    @ShouldSilenceNotifications BIT = 0,
    @UpdatedAt DATETIME2 = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.UserCognitiveStates
    SET
        SamplesCollected = @SamplesCollected,
        Phase = @Phase,
        State = @State,
        CognitiveLoadScore = @CognitiveLoadScore,
        BaselineScore = @BaselineScore,
        ComparisonBaselineScore = @ComparisonBaselineScore,
        ShouldSilenceNotifications = @ShouldSilenceNotifications,
        UpdatedAt = @UpdatedAt
    WHERE UserId = @UserId;

    IF @@ROWCOUNT = 0
    BEGIN
        INSERT INTO dbo.UserCognitiveStates (
            UserId, SamplesCollected, Phase, State,
            CognitiveLoadScore, BaselineScore, ComparisonBaselineScore,
            ShouldSilenceNotifications, UpdatedAt
        )
        VALUES (
            @UserId, @SamplesCollected, @Phase, @State,
            @CognitiveLoadScore, @BaselineScore, @ComparisonBaselineScore,
            @ShouldSilenceNotifications, @UpdatedAt
        );
    END;

    SELECT
        UserId, SamplesCollected, Phase, State,
        CognitiveLoadScore, BaselineScore, ComparisonBaselineScore,
        ShouldSilenceNotifications, UpdatedAt
    FROM dbo.UserCognitiveStates
    WHERE UserId = @UserId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.DeleteUserCognitiveState
    @UserId NVARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.UserCognitiveStates
    WHERE UserId = @UserId;

    SELECT @@ROWCOUNT AS DeletedCount;
END;
GO
