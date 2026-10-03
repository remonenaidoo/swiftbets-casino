UPDATE catalog.games
SET category_key = COALESCE(@Category, category_key), position = COALESCE(@Position, position)
WHERE game_id = @GameId;
