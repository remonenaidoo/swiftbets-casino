INSERT INTO catalog.favourites (punter_id, game_id)
SELECT @PunterId, @GameId WHERE EXISTS (SELECT 1 FROM catalog.games WHERE game_id = @GameId)
ON CONFLICT DO NOTHING;
SELECT EXISTS (SELECT 1 FROM catalog.games WHERE game_id = @GameId);
