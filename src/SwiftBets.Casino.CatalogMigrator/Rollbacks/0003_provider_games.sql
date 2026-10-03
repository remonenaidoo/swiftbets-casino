-- Rolls back 0003_provider_games. Favourites and synced Pragmatic games are lost; the next sync after re-applying restores the games.
DROP TABLE IF EXISTS catalog.favourites;
DELETE FROM catalog.game_markets WHERE game_id IN (SELECT game_id FROM catalog.games WHERE provider_id = 'pragmatic');
DELETE FROM catalog.games WHERE provider_id = 'pragmatic';
DELETE FROM catalog.providers WHERE provider_id = 'pragmatic';
DELETE FROM catalog.categories WHERE category_key IN ('table', 'other');
ALTER TABLE catalog.games DROP COLUMN IF EXISTS synced_at, DROP COLUMN IF EXISTS demo_available, DROP COLUMN IF EXISTS image_url;
