CREATE DATABASE TaskFlowDb;
GO

USE TaskFlowDb;
GO

CREATE TABLE Users (
    Id INT PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    Role INT NOT NULL -- Enum: 0=Owner, 1=Director, 2=Manager, 3=Supervisor, 4=Employee
);
GO

CREATE TABLE Tasks (
    Id INT IDENTITY PRIMARY KEY,
    Title NVARCHAR(200) NOT NULL,
    Description NVARCHAR(MAX) NOT NULL,
    Comment NVARCHAR(MAX),
    CreatedByUserId INT NOT NULL,
    AssignedToUserId INT NOT NULL,
    Status INT NOT NULL, -- Enum: 0=Pending, 1=InProgress, 2=Completed
    CreatedAt DATETIME NOT NULL,
    CompletedAt DATETIME NULL,

    FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id),
    FOREIGN KEY (AssignedToUserId) REFERENCES Users(Id)
);
GO

CREATE TABLE Notifications (
    Id INT IDENTITY PRIMARY KEY,
    UserId INT NOT NULL,
    Message NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIME NOT NULL,

    FOREIGN KEY (UserId) REFERENCES Users(Id)
);
GO

-- Seed 5 users
INSERT INTO Users (Id, Name, Role) VALUES
(1, 'CEO', 0),
(2, 'Director', 1),
(3, 'Manager', 2),
(4, 'Supervisor', 3),
(5, 'Employee', 4);
GO