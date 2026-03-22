"""
Shared configuration for the RAG pipeline.

Switch between OpenAI and Ollama by setting PROVIDER in your .env file:
    PROVIDER=openai   → uses OpenAI API (requires OPENAI_API_KEY)
    PROVIDER=ollama   → uses local Ollama models (free, no API key needed)

You can also customize which models to use via environment variables.
"""

import os
from dotenv import load_dotenv

load_dotenv()

# --- PROVIDER CONFIG ---
PROVIDER = os.getenv("PROVIDER", "openai").lower()

# --- MODEL NAMES (override via .env if you want) ---
OPENAI_EMBEDDING_MODEL = os.getenv("OPENAI_EMBEDDING_MODEL", "text-embedding-3-small")
OPENAI_CHAT_MODEL = os.getenv("OPENAI_CHAT_MODEL", "gpt-4o-mini")
OLLAMA_EMBEDDING_MODEL = os.getenv("OLLAMA_EMBEDDING_MODEL", "nomic-embed-text")
OLLAMA_CHAT_MODEL = os.getenv("OLLAMA_CHAT_MODEL", "llama3.2")

# --- PATH CONFIG ---
DOCS_DIR = "docs"
CHROMA_DIR = "chroma_db"
CHUNK_SIZE = 1000
CHUNK_OVERLAP = 200

# --- MEMORY CONFIG ---
# Number of past conversation turns to keep in memory.
# Higher = more context but uses more tokens per request.
MEMORY_WINDOW = 5

# --- RE-RANKER CONFIG ---
# When enabled, the retriever over-fetches candidates and a cross-encoder
# re-ranker scores them for relevance, keeping only the top results.
# This improves answer quality by filtering out marginally-relevant chunks.
RERANKER_ENABLED = os.getenv("RERANKER_ENABLED", "true").lower() == "true"

# How many candidates the retriever fetches BEFORE re-ranking.
# More candidates = better chance of finding the best chunks, but slower.
RERANKER_CANDIDATES = int(os.getenv("RERANKER_CANDIDATES", "40"))

# How many chunks survive after re-ranking (sent to the LLM).
# Set higher than the retriever's k to keep diversity while still re-ordering.
RERANKER_TOP_K = int(os.getenv("RERANKER_TOP_K", "15"))

# FlashRank model - "ms-marco-MiniLM-L-12-v2" is the default, good balance
# of speed and quality.
RERANKER_MODEL = os.getenv("RERANKER_MODEL", "ms-marco-MiniLM-L-12-v2")


def get_reranker():
    """
    Return a FlashRank re-ranker compressor, or None if disabled.

    The re-ranker is a small cross-encoder model that scores each retrieved
    chunk against the original question. Unlike embeddings (which compare
    meaning in abstract vector space), a cross-encoder reads the question
    and chunk TOGETHER, producing a much more accurate relevance score.

    This lets us over-fetch from the retriever (cast a wide net) and then
    keep only the truly relevant chunks.
    """
    if not RERANKER_ENABLED:
        return None
    from langchain.retrievers.document_compressors import FlashrankRerank
    return FlashrankRerank(
        model=RERANKER_MODEL,
        top_n=RERANKER_TOP_K,
    )


def get_embeddings():
    """Return the embedding model based on the configured provider."""
    if PROVIDER == "ollama":
        from langchain_ollama import OllamaEmbeddings
        return OllamaEmbeddings(model=OLLAMA_EMBEDDING_MODEL)
    else:
        from langchain_openai import OpenAIEmbeddings
        return OpenAIEmbeddings(model=OPENAI_EMBEDDING_MODEL)


def get_llm():
    """Return the chat model based on the configured provider."""
    if PROVIDER == "ollama":
        from langchain_ollama import ChatOllama
        return ChatOllama(model=OLLAMA_CHAT_MODEL)
    else:
        from langchain_openai import ChatOpenAI
        return ChatOpenAI(model=OPENAI_CHAT_MODEL, temperature=0)
