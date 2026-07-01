-- Seeds the three roles (Read/Manage/Admin) and an initial admin user.
-- Runs after 01-identity-schema.sql on first database initialization
-- (docker-entrypoint-initdb.d). Idempotent: re-running inserts nothing new.
--
-- The admin password is "ChangeMe!2026" (PBKDF2 hash below, produced by
-- ASP.NET Core Identity's PasswordHasher). CHANGE IT after first login.

INSERT INTO roles (id, name, normalized_name, concurrency_stamp)
VALUES
    ('11111111-1111-1111-1111-111111111111', 'Read',   'READ',   gen_random_uuid()::text),
    ('22222222-2222-2222-2222-222222222222', 'Manage', 'MANAGE', gen_random_uuid()::text),
    ('33333333-3333-3333-3333-333333333333', 'Admin',  'ADMIN',  gen_random_uuid()::text)
ON CONFLICT DO NOTHING;

INSERT INTO users (
    id, user_name, normalized_user_name,
    email, normalized_email, email_confirmed,
    password_hash, security_stamp, concurrency_stamp,
    phone_number, phone_number_confirmed, two_factor_enabled,
    lockout_end, lockout_enabled, access_failed_count)
VALUES (
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'admin', 'ADMIN',
    NULL, NULL, FALSE,
    'AQAAAAIAAYagAAAAELQBnu4Ed0DK2YXXX+GHuNP66qv3AJT0TzXv8MGmKeodGgIQFzO7xqZomehAwMYRZw==',
    gen_random_uuid()::text, gen_random_uuid()::text,
    NULL, FALSE, FALSE,
    NULL, TRUE, 0)
ON CONFLICT DO NOTHING;

INSERT INTO user_roles (user_id, role_id)
VALUES ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', '33333333-3333-3333-3333-333333333333')
ON CONFLICT DO NOTHING;
