-- Games synced from a provider's list carry artwork by URL and whether a free demo exists.
ALTER TABLE catalog.games ADD COLUMN image_url text NULL;
ALTER TABLE catalog.games ADD COLUMN demo_available boolean NOT NULL DEFAULT false;
ALTER TABLE catalog.games ADD COLUMN synced_at timestamptz NULL;

INSERT INTO catalog.providers (provider_id, name, wallet_model) VALUES ('pragmatic', 'Pragmatic Play', 'seamless');

INSERT INTO catalog.categories (category_key, name, position) VALUES
    ('table', 'Table games', 4),
    ('other', 'More games', 5);

-- A player's favourite games.
CREATE TABLE catalog.favourites
(
    punter_id  uuid        NOT NULL,
    game_id    text        NOT NULL REFERENCES catalog.games (game_id) ON DELETE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (punter_id, game_id)
);
