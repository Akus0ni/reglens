-- 004_contextual_retrieval: generated chunk contexts and the pipeline that built each document (spec 0005, P5, P6).

-- Contexts keyed by the SHA-256 of everything that went into them (prompt version, model, document text, heading,
-- para_ref, chunk text). Written as each context arrives, outside the document transaction (D54), so a failed or
-- repeated run reuses them instead of paying again.
CREATE TABLE chunk_contexts (
    input_hash         char(64)    PRIMARY KEY,
    model              text        NOT NULL,
    prompt_version     text        NOT NULL,
    context            text        NOT NULL,
    input_tokens       int         NOT NULL,
    cache_write_tokens int         NOT NULL,
    cache_read_tokens  int         NOT NULL,
    output_tokens      int         NOT NULL,
    created_at         timestamptz NOT NULL DEFAULT now()
);

-- "chunker 2; context claude-haiku-5-5 v1" or "chunker 2; no context". Documents ingested before this migration get
-- '' and so differ from any pipeline: the next ingest rebuilds them without --force.
ALTER TABLE documents ADD COLUMN pipeline text NOT NULL DEFAULT '';
