"""
Step 1: Ingest documents into the vector store.

This script:
1. Loads all documents from the docs/ folder (.txt, .md, .pdf)
2. Tags each document with metadata (source folder = category)
3. Splits them into smaller chunks
4. Creates embeddings (OpenAI or Ollama - set PROVIDER in .env)
5. Stores everything in a local ChromaDB database

Folder structure for organized docs:
    docs/
    ├── dynamics/       ← Dynamics 365 docs
    ├── power-platform/ ← Power Platform docs
    ├── azure/          ← Azure docs
    └── sample.txt      ← loose files work too

Run modes:
    python ingest.py             # full re-ingest (wipes DB, re-embeds everything)
    python ingest.py --update    # incremental - only embeds NEW files not yet in DB
"""

import os
import re
import sys
import shutil
from langchain_community.document_loaders import DirectoryLoader, TextLoader
from langchain_community.document_loaders import PyPDFLoader
from langchain.text_splitter import RecursiveCharacterTextSplitter
from langchain.schema import Document
from langchain_chroma import Chroma
from config import get_embeddings, DOCS_DIR, CHROMA_DIR, CHUNK_SIZE, CHUNK_OVERLAP, PROVIDER


def clean_markdown(text):
    """
    Strip out noise from Microsoft docs markdown files.

    Removes YAML frontmatter, include tags, image references,
    and excessive whitespace so that only the useful content
    gets embedded.
    """
    # Remove YAML frontmatter (--- ... ---)
    text = re.sub(r'^---\s*\n.*?\n---\s*\n', '', text, flags=re.DOTALL)
    # Remove [!INCLUDE...] tags (both simple and nested markdown link format)
    text = re.sub(r'\[!INCLUDE.*?\]', '', text)
    text = re.sub(r'\[!include\s*\[.*?\]\(.*?\)\]', '', text, flags=re.IGNORECASE)
    # Remove standalone include references like (includes/prod_short.md)]
    text = re.sub(r'\(includes/[^)]+\.md\)\]?', '', text)
    # Remove image references ![alt](path)
    text = re.sub(r'!\[.*?\]\(.*?\)', '', text)
    # Remove HTML comments
    text = re.sub(r'<!--.*?-->', '', text, flags=re.DOTALL)
    # Remove excessive blank lines
    text = re.sub(r'\n{3,}', '\n\n', text)
    return text.strip()


def load_markdown_files(docs_dir):
    """Load .md files with custom cleaning instead of UnstructuredMarkdownLoader."""
    documents = []
    for root, dirs, files in os.walk(docs_dir):
        for file in files:
            if file.endswith(".md"):
                filepath = os.path.join(root, file)
                try:
                    with open(filepath, "r", encoding="utf-8", errors="ignore") as f:
                        raw = f.read()
                    cleaned = clean_markdown(raw)
                    if len(cleaned) > 50:  # skip near-empty files
                        documents.append(Document(
                            page_content=cleaned,
                            metadata={"source": filepath},
                        ))
                except Exception:
                    pass  # skip files that can't be read
    return documents


# Map file extensions to their LangChain loaders (for non-markdown files)
LOADERS = {
    "**/*.txt": TextLoader,
    "**/*.pdf": PyPDFLoader,
}


def get_category(file_path):
    """
    Determine the category (source) from the file's folder name.

    If a file is in docs/dynamics/some-file.md → category is "dynamics"
    If a file is in docs/azure/networking/vnet.md → category is "azure"
    If a file is in docs/sample.txt (no subfolder) → category is "general"
    """
    # Get the path relative to the docs directory
    rel_path = os.path.relpath(file_path, DOCS_DIR)
    parts = rel_path.split(os.sep)

    # If file is in a subfolder, use the first folder as category
    if len(parts) > 1:
        return parts[0]
    return "general"


def get_existing_sources(vectorstore):
    """
    Get the set of source file paths already in ChromaDB.

    Scans the entire DB in batches to collect all unique 'source' metadata values.
    This lets us skip files that are already embedded during incremental updates.
    """
    collection = vectorstore._collection
    total = collection.count()
    sources = set()
    batch_size = 5000
    for offset in range(0, total, batch_size):
        batch = collection.get(include=["metadatas"], limit=batch_size, offset=offset)
        for m in batch["metadatas"]:
            sources.add(m.get("source", ""))
    return sources


def main():
    # Check for --update flag (incremental mode)
    incremental = "--update" in sys.argv

    print(f"🔧 Using provider: {PROVIDER}")
    print(f"📋 Mode: {'INCREMENTAL UPDATE' if incremental else 'FULL RE-INGEST'}")
    print()

    # 1. LOAD DOCUMENTS
    # We load markdown files with our custom cleaner (strips frontmatter/noise),
    # and use LangChain loaders for .txt and .pdf files.
    print(f"📂 Loading documents from '{DOCS_DIR}/'...")
    all_documents = []

    # Load markdown files with custom cleaning
    md_docs = load_markdown_files(DOCS_DIR)
    if md_docs:
        print(f"   Loaded {len(md_docs)} .md file(s)")
        all_documents.extend(md_docs)

    # Load other file types with LangChain loaders
    for glob_pattern, loader_cls in LOADERS.items():
        loader = DirectoryLoader(
            DOCS_DIR,
            glob=glob_pattern,
            loader_cls=loader_cls,
            silent_errors=True,
        )
        docs = loader.load()
        if docs:
            file_type = glob_pattern.split("*.")[-1]
            print(f"   Loaded {len(docs)} .{file_type} file(s)")
            all_documents.extend(docs)

    if not all_documents:
        print("   No documents found! Add files to the docs/ folder.")
        return

    print(f"   Total: {len(all_documents)} document(s)")

    # 2. ADD METADATA
    # Tag each document with its category based on which subfolder it's in.
    # This lets you filter queries later (e.g., "only search Dynamics docs").
    print("🏷️  Tagging documents with metadata...")
    categories = set()
    for doc in all_documents:
        category = get_category(doc.metadata.get("source", ""))
        doc.metadata["category"] = category
        categories.add(category)
    print(f"   Categories found: {', '.join(sorted(categories))}")

    embeddings = get_embeddings()

    # 3. INCREMENTAL vs FULL mode
    if incremental and os.path.exists(CHROMA_DIR):
        # INCREMENTAL: only embed files not already in the DB
        print("🔍 Scanning existing vector store for already-ingested files...")
        vectorstore = Chroma(
            persist_directory=CHROMA_DIR,
            embedding_function=embeddings,
        )
        existing_sources = get_existing_sources(vectorstore)
        print(f"   Found {len(existing_sources)} files already in DB")

        # Filter to only new documents
        new_documents = [
            doc for doc in all_documents
            if doc.metadata.get("source", "") not in existing_sources
        ]
        print(f"   New files to ingest: {len(new_documents)}")

        if not new_documents:
            print("✅ Nothing new to ingest - vector store is up to date!")
            return

        # Split only the new documents into chunks
        print(f"✂️  Splitting into chunks (size={CHUNK_SIZE}, overlap={CHUNK_OVERLAP})...")
        text_splitter = RecursiveCharacterTextSplitter(
            chunk_size=CHUNK_SIZE,
            chunk_overlap=CHUNK_OVERLAP,
        )
        chunks = text_splitter.split_documents(new_documents)
        print(f"   Created {len(chunks)} new chunks")

        # Add to existing vector store in batches (no wipe!)
        # OpenAI has a max tokens-per-request limit, so we batch to stay under it.
        print("🧮 Creating embeddings for new chunks and adding to ChromaDB...")
        batch_size = 500  # safe batch size for OpenAI embeddings
        for i in range(0, len(chunks), batch_size):
            batch = chunks[i:i + batch_size]
            vectorstore.add_documents(batch)
            print(f"   Embedded {min(i + batch_size, len(chunks))}/{len(chunks)} chunks...")
        total = vectorstore._collection.count()
        print(f"✅ Done! Added {len(chunks)} chunks (total now: {total})")

    else:
        # FULL RE-INGEST: wipe and rebuild from scratch
        # 3. SPLIT INTO CHUNKS
        print(f"✂️  Splitting into chunks (size={CHUNK_SIZE}, overlap={CHUNK_OVERLAP})...")
        text_splitter = RecursiveCharacterTextSplitter(
            chunk_size=CHUNK_SIZE,
            chunk_overlap=CHUNK_OVERLAP,
        )
        chunks = text_splitter.split_documents(all_documents)
        print(f"   Created {len(chunks)} chunks")

        # 4. CLEAR OLD DATA AND CREATE FRESH VECTOR STORE
        if os.path.exists(CHROMA_DIR):
            shutil.rmtree(CHROMA_DIR)
            print(f"🗑️  Cleared old vector store")

        # 5. CREATE EMBEDDINGS AND STORE IN VECTOR DB
        # Batch the inserts to avoid silent truncation with large datasets.
        print("🧮 Creating embeddings and storing in ChromaDB...")
        batch_size = 2000  # keep under OpenAI's 300K token-per-request limit
        vectorstore = None
        for i in range(0, len(chunks), batch_size):
            batch = chunks[i:i + batch_size]
            if vectorstore is None:
                vectorstore = Chroma.from_documents(
                    documents=batch,
                    embedding=embeddings,
                    persist_directory=CHROMA_DIR,
                )
            else:
                vectorstore.add_documents(batch)
            print(f"   Embedded {min(i + batch_size, len(chunks))}/{len(chunks)} chunks...")
        print(f"✅ Done! Stored {vectorstore._collection.count()} chunks in '{CHROMA_DIR}/'")

    print()
    print("Next step: run 'python query.py' to ask questions!")


if __name__ == "__main__":
    main()
