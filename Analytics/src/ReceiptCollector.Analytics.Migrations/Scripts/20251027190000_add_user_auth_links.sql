CREATE TABLE IF NOT EXISTS user_auth_links
(
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash CHARACTER VARYING(256) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    used_at TIMESTAMPTZ NULL,
    CONSTRAINT unique_user_auth_link UNIQUE (user_id),
    CONSTRAINT unique_user_auth_link_token UNIQUE (token_hash)
);
