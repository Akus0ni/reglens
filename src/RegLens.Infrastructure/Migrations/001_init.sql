-- 001_init: documents and chunks (spec 0001, R1.8).
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE documents (
    id          text        PRIMARY KEY,
    title       text        NOT NULL,
    reference   text        NOT NULL,
    version     text        NOT NULL,
    source_url  text        NOT NULL,
    sha256      char(64)    NOT NULL,
    ingested_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE chunks (
    id             bigserial   PRIMARY KEY,
    document_id    text        NOT NULL REFERENCES documents (id) ON DELETE CASCADE,
    para_ref       text        NOT NULL,
    heading        text        NOT NULL,
    page_start     int         NOT NULL,
    page_end       int         NOT NULL CHECK (page_end >= page_start),
    content        text        NOT NULL,
    context        text,                                   -- filled by contextual retrieval (spec 0004)
    content_hash   char(64)    NOT NULL UNIQUE,            -- idempotent insert key (P4)
    token_estimate int         NOT NULL,
    embedding      vector(768) NOT NULL,                   -- nomic-embed-text
    tsv            tsvector    GENERATED ALWAYS AS
                   (to_tsvector('english', coalesce(context, '') || ' ' || content)) STORED,
    created_at     timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX chunks_embedding_hnsw ON chunks USING hnsw (embedding vector_cosine_ops);
CREATE INDEX chunks_tsv_gin ON chunks USING gin (tsv);
CREATE INDEX chunks_document_id ON chunks (document_id);
