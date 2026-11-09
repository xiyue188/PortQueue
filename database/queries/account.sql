/* 认证所需最小账号信息；按唯一用户名或主键查询，不接受动态列或排序。 */
SELECT u.user_id AS UserId,u.username AS Username,u.display_name AS DisplayName,
       u.role_code AS RoleCode,u.status_code AS StatusCode,r.status_code AS RoleStatus,
       u.password_hash AS PasswordHash
FROM dbo.user_account u
JOIN dbo.role r ON r.role_code=u.role_code
WHERE (@UserId IS NOT NULL AND u.user_id=@UserId)
   OR (@UserId IS NULL AND u.username=@Username);
