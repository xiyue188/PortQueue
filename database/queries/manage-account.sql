/* 本机管理员命令使用。应用角色仍禁止直接写表；账号与不含密码的审计在同一事务提交。
   初次引导可自归属操作者，同时快照保存 ORIGINAL_LOGIN() 说明本机执行身份。 */
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @id bigint,@before nvarchar(max),@after nvarchar(max);
SELECT @id=user_id FROM dbo.user_account WITH(UPDLOCK,HOLDLOCK) WHERE username=@Username;
IF @Action='create'
BEGIN
    IF @id IS NOT NULL THROW 53100,'Account already exists.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.role WITH(HOLDLOCK) WHERE role_code=@RoleCode AND status_code='ENABLED')
        THROW 53101,'Role is unavailable.',1;
    INSERT dbo.user_account(username,display_name,role_code,password_hash,status_code)
    VALUES(@Username,@DisplayName,@RoleCode,@PasswordHash,'ENABLED');
    SET @id=SCOPE_IDENTITY();
END
ELSE
BEGIN
    IF @id IS NULL THROW 53102,'Account was not found.',1;
    SELECT @before=(SELECT username,display_name,role_code,status_code FROM dbo.user_account WHERE user_id=@id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
    IF @Action='reset-password' UPDATE dbo.user_account SET password_hash=@PasswordHash WHERE user_id=@id;
    ELSE IF @Action='disable' UPDATE dbo.user_account SET status_code='DISABLED' WHERE user_id=@id;
    ELSE IF @Action='enable' UPDATE dbo.user_account SET status_code='ENABLED' WHERE user_id=@id;
    ELSE THROW 53103,'Unknown account operation.',1;
END;
SELECT @after=(SELECT username,display_name,role_code,status_code,@Action AS account_operation,ORIGINAL_LOGIN() AS database_operator
    FROM dbo.user_account WHERE user_id=@id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
INSERT dbo.audit_log(table_name,record_id,operation_type,old_data_json,new_data_json,operator_id)
VALUES('user_account',CONVERT(varchar(100),@id),CASE WHEN @Action='create' THEN 'INSERT' ELSE 'UPDATE' END,@before,@after,@id);
COMMIT TRANSACTION;
SELECT @id;
