-- Default home + admin membership, so the seeded admin can sign in with a home.
-- Runs after 02-identity-seed (admin user) and 03-home-schema (tables).
INSERT INTO home.homes (name) VALUES ('Home') ON CONFLICT (name) DO NOTHING;

INSERT INTO home.user_homes (user_id, home_id)
SELECT 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', id FROM home.homes WHERE name = 'Home'
ON CONFLICT (user_id) DO NOTHING;