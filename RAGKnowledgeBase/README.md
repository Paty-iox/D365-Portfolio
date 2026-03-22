# RAG Knowledge Base

A Retrieval-Augmented Generation (RAG) system built with LangChain for querying Microsoft Dynamics 365, Power Platform, and Azure documentation. Features hybrid search (vector + keyword), FlashRank re-ranking, conversation memory, and a Streamlit web UI.

## Features

- **Hybrid search** - combines vector similarity (ChromaDB) with BM25 keyword matching for better retrieval
- **FlashRank re-ranker** - cross-encoder model scores and re-orders retrieved chunks by relevance
- **Conversation memory** - follow-up questions work ("tell me more", "how does that compare")
- **Deep analysis mode** - structured answers with trade-offs, risks, and implementation details
- **Streamlit web UI** - chat interface with source display, settings, and memory indicator
- **CLI mode** - interactive terminal for quick queries
- **Provider switching** - use OpenAI API or local Ollama models
- **Microsoft docs fetcher** - downloads official docs from GitHub for Power Platform, Dynamics 365, and Azure

## Architecture

```
Question
  |
  v
[Hybrid Retriever]
  |-- Vector search (ChromaDB + OpenAI embeddings)
  |-- BM25 keyword search
  |
  v
[FlashRank Re-Ranker] (optional)
  |-- Scores each chunk against the question
  |-- Keeps top 15 most relevant
  |
  v
[format_docs] - filters noise, joins chunks
  |
  v
[LLM] (gpt-4o-mini or Ollama)
  |-- Includes conversation history
  |-- Uses enhanced architect-level prompt
  |
  v
Answer with sources
```

## Prerequisites

- Python 3.10+
- An OpenAI API key (or [Ollama](https://ollama.com) installed locally for free usage)
- ~2GB disk space for the vector database after ingestion

## Quick Start

### 1. Clone and install

```bash
git clone https://github.com/YOUR_USERNAME/rag-knowledge-base.git
cd rag-knowledge-base
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
```

### 2. Configure

```bash
cp .env.example .env
```

Edit `.env` and add your OpenAI API key:

```
PROVIDER=openai
OPENAI_API_KEY=your-key-here
```

Or use Ollama for free local inference:

```
PROVIDER=ollama
```

### 3. Fetch documentation

Download Microsoft docs from GitHub:

```bash
python fetch_docs.py
```

This fetches Power Platform, Dynamics 365, Azure, and related docs (~25,000 markdown files). Select which doc sets to download when prompted.

### 4. Ingest into vector store

```bash
python ingest.py
```

This cleans the markdown, splits into chunks, creates embeddings, and stores in ChromaDB. Takes a few minutes depending on the number of docs.

For incremental updates (only new files):

```bash
python ingest.py --update
```

### 5. Run

**Web UI (recommended):**

```bash
streamlit run app.py
```

Opens at http://localhost:8501

**CLI mode:**

```bash
python query.py
```

## Configuration

All settings are in `.env`. See `.env.example` for the full list:

| Variable | Default | Description |
|----------|---------|-------------|
| `PROVIDER` | `openai` | `openai` or `ollama` |
| `OPENAI_API_KEY` | - | Your OpenAI API key |
| `OPENAI_CHAT_MODEL` | `gpt-4o-mini` | Chat model for answers |
| `OPENAI_EMBEDDING_MODEL` | `text-embedding-3-small` | Embedding model |
| `RERANKER_ENABLED` | `true` | Enable FlashRank re-ranking |
| `RERANKER_CANDIDATES` | `40` | Chunks to fetch before re-ranking |
| `RERANKER_TOP_K` | `15` | Chunks to keep after re-ranking |

## Project Structure

```
rag-knowledge-base/
  app.py              # Streamlit web UI
  query.py            # CLI query interface
  ingest.py           # Document ingestion pipeline
  fetch_docs.py       # Microsoft docs downloader
  config.py           # Shared configuration
  requirements.txt    # Python dependencies
  .env.example        # Environment variable template
  .streamlit/         # Streamlit theme config
  test_*.py           # Test suite
```

## Testing

Run the test suite to verify the system:

```bash
# Test retrieval quality (standard vs re-ranked)
python test_reranker.py

# Test prompt quality (reasoning, trade-offs)
python test_prompt_quality.py

# Test conversation memory
python test_memory.py

# Test deep analysis mode
python test_deep_mode.py

# Full RAG accuracy test (40 questions)
python test_rag.py
```

## How It Works

1. **Ingestion** (`ingest.py`): Loads markdown files, strips YAML frontmatter and noise, splits into 1000-char chunks with 200-char overlap, creates OpenAI embeddings, stores in ChromaDB.

2. **Retrieval** (`query.py` / `app.py`): Uses EnsembleRetriever combining vector similarity search with BM25 keyword matching. Optionally re-ranks results with FlashRank cross-encoder for better precision.

3. **Generation**: Sends the top chunks plus conversation history to the LLM with an enhanced prompt that encourages architectural reasoning, trade-offs, and practical details.

4. **Memory**: ConversationBufferWindowMemory keeps the last 5 turns so follow-up questions resolve correctly ("tell me more about that", "how does it compare").

## License

MIT
