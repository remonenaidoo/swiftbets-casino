INSERT INTO catalog.game_markets (game_id, market, enabled)
SELECT @GameId, @Market, @Enabled WHERE EXISTS (SELECT 1 FROM catalog.games WHERE game_id = @GameId)
ON CONFLICT (game_id, market) DO UPDATE SET enabled = EXCLUDED.enabled;
