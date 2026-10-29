/*
   模块：数据库初始化。
   作用：创建 PortQueue 数据库并设置本项目需要的基础选项。
   执行：首次部署时执行；后续脚本依赖本数据库上下文。
*/
USE [master];
GO

IF DB_ID(N'PortQueue') IS NULL
BEGIN
    CREATE DATABASE [PortQueue];
END;
GO

ALTER DATABASE [PortQueue] SET READ_COMMITTED_SNAPSHOT ON;
ALTER DATABASE [PortQueue] SET RECOVERY SIMPLE;
GO

USE [PortQueue];
GO

IF SCHEMA_ID(N'dbo') IS NULL
    EXEC(N'CREATE SCHEMA dbo');
GO
