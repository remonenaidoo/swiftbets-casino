-- New games join the end of their category, shown or hidden as configured; a known game only gets its name, artwork and demo flag refreshed.
WITH inserted AS (
    INSERT INTO catalog.games (game_id, name, category_key, provider_id, tag, min_bet, currency, position, image_url, demo_available, synced_at)
    SELECT @GameId, @Name, @Category, @ProviderId, NULL, @MinBet, @Currency,
           COALESCE((SELECT MAX(position) FROM catalog.games WHERE category_key = @Category), 0) + 1, @ImageUrl, @DemoAvailable, now()
    ON CONFLICT (game_id) DO UPDATE SET name = EXCLUDED.name, image_url = EXCLUDED.image_url, demo_available = EXCLUDED.demo_available, synced_at = now()
        WHERE catalog.games.provider_id = EXCLUDED.provider_id
    RETURNING (xmax = 0) AS is_new
), market AS (
    INSERT INTO catalog.game_markets (game_id, market, enabled)
    SELECT @GameId, @Market, @Show FROM inserted WHERE is_new
    ON CONFLICT DO NOTHING
)
SELECT COALESCE((SELECT is_new FROM inserted), false);
