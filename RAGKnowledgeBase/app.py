"""
Phase 4: Streamlit Web UI for the RAG Knowledge Base.

Run with:
    streamlit run app.py
"""

import streamlit as st
import time
import re
from langchain_chroma import Chroma
from langchain.prompts import ChatPromptTemplate
from langchain.schema.runnable import RunnableLambda
from langchain.schema.output_parser import StrOutputParser
from langchain.retrievers import EnsembleRetriever, ContextualCompressionRetriever
from langchain_community.retrievers import BM25Retriever
from langchain.memory import ConversationBufferWindowMemory
from langchain.schema import Document
from config import (
    get_embeddings, get_llm, CHROMA_DIR, PROVIDER,
    MEMORY_WINDOW, CHUNK_SIZE, CHUNK_OVERLAP,
    RERANKER_ENABLED, RERANKER_CANDIDATES, RERANKER_TOP_K, get_reranker,
)

# ─── Page Config ───────────────────────────────────────────────
st.set_page_config(
    page_title="RAG Knowledge Base",
    page_icon="📚",
    layout="wide",
    initial_sidebar_state="expanded",
)

# ─── Custom CSS ────────────────────────────────────────────────
st.markdown("""
<style>
    /* Hide Streamlit defaults for a cleaner look */
    #MainMenu {visibility: hidden;}
    header {visibility: hidden;}
    footer {visibility: hidden;}
    .stDeployButton {display: none;}

    /* Claude color scheme - warm terracotta accent on parchment dark */
    :root {
        --accent: #DA7756;
        --accent-dim: #C96A4A;
        --accent-glow: rgba(218,119,86,0.12);
        --accent-border: rgba(218,119,86,0.25);
        --bg: #1A1915;
        --bg-sidebar: #2A2823;
        --surface: rgba(255,255,255,0.04);
        --surface-hover: rgba(255,255,255,0.07);
        --border-subtle: rgba(255,255,255,0.08);
        --text-primary: #E8E5E0;
        --text-dim: rgba(232,229,224,0.4);
        --text-secondary: rgba(232,229,224,0.6);
    }

    /* Override Streamlit's default background */
    .stApp {
        background-color: var(--bg) !important;
    }

    /* Tighten main area padding */
    .main .block-container {
        padding-top: 2rem;
        padding-bottom: 1rem;
        max-width: 900px;
    }

    /* Sidebar styling */
    section[data-testid="stSidebar"] {
        background-color: #2A2823 !important;
        border-right: 1px solid var(--border-subtle);
    }
    section[data-testid="stSidebar"] [data-testid="stSidebarContent"] {
        background-color: #2A2823 !important;
    }
    section[data-testid="stSidebar"] .block-container {
        padding-top: 1.5rem;
    }

    /* Progress bar accent color */
    .stProgress > div > div > div {
        background-color: var(--accent) !important;
    }

    /* Chat message styling */
    .stChatMessage {
        border-radius: 12px !important;
        margin-bottom: 0.5rem !important;
    }

    /* Assistant messages - warm accent left border */
    .stChatMessage[data-testid="stChatMessage"]:has([data-testid="chatAvatarIcon-assistant"]) {
        border-left: 3px solid var(--accent) !important;
        background: rgba(218,119,86,0.03) !important;
    }

    /* Source expander styling */
    .streamlit-expanderHeader {
        font-size: 0.8rem !important;
        color: var(--text-secondary) !important;
    }
    details[data-testid="stExpander"] {
        border: 1px solid var(--border-subtle) !important;
        border-radius: 8px !important;
        background: transparent !important;
    }

    /* Chat input styling */
    .stChatInput {
        border-color: var(--border-subtle) !important;
    }
    .stChatInput:focus-within {
        border-color: var(--accent) !important;
    }

    /* Button styling - pill chips (force override Streamlit's inline styles) */
    .stApp .stButton > button,
    .stApp .stButton > button[kind="secondary"],
    .stApp [data-testid="stBaseButton-secondary"],
    .main .stButton > button {
        border-radius: 24px !important;
        border: 1px solid rgba(255,255,255,0.1) !important;
        background-color: rgba(255,255,255,0.04) !important;
        background: rgba(255,255,255,0.04) !important;
        color: #E8E5E0 !important;
        transition: all 0.25s ease !important;
        font-size: 0.85rem !important;
    }
    .stApp .stButton > button:hover,
    .main .stButton > button:hover {
        border-color: #DA7756 !important;
        background-color: rgba(218,119,86,0.08) !important;
        background: rgba(218,119,86,0.08) !important;
        box-shadow: 0 0 16px rgba(218,119,86,0.12) !important;
        color: #E8E5E0 !important;
    }
    .stApp .stButton > button:focus,
    .stApp .stButton > button:active,
    .main .stButton > button:focus,
    .main .stButton > button:active {
        background-color: rgba(255,255,255,0.06) !important;
        background: rgba(255,255,255,0.06) !important;
        border-color: #DA7756 !important;
        color: #E8E5E0 !important;
        box-shadow: none !important;
    }

    /* Primary button (Clear Conversation) - ghost outline */
    .stButton > button[kind="primary"] {
        background: transparent !important;
        border: 1px solid var(--accent) !important;
        color: var(--accent) !important;
        border-radius: 24px !important;
    }
    .stButton > button[kind="primary"]:hover {
        background: var(--accent-glow) !important;
        box-shadow: 0 0 16px rgba(218,119,86,0.15) !important;
        color: var(--text-primary) !important;
    }

    /* Slider - gold accent */
    .stSlider [data-testid="stThumbValue"] {
        color: var(--accent) !important;
    }
    .stSlider > div > div > div > div {
        background-color: var(--accent) !important;
    }
    .stSlider [role="slider"] {
        background-color: var(--accent) !important;
    }

    /* Toggle - gold accent (override all Streamlit toggle internals) */
    [data-testid="stToggle"] label span[data-checked="true"],
    .st-emotion-cache-1m6wrpk,
    [data-testid="stBaseButton-secondary"] {
        background-color: var(--accent) !important;
    }

    /* Force slider filled track to gold */
    .stSlider [data-testid="stSliderTrack"] > div:first-child {
        background: var(--accent) !important;
    }
    /* Slider track active portion */
    div[data-baseweb="slider"] div[role="slider"] {
        background-color: var(--accent) !important;
        border-color: var(--accent) !important;
    }
    /* Override any red/primary color in slider */
    .stSlider div[data-testid="stTickBarMin"],
    .stSlider div[data-testid="stTickBarMax"] {
        color: var(--text-dim) !important;
    }

    /* Links - gold */
    a {
        color: var(--accent) !important;
    }
    a:hover {
        color: var(--accent-dim) !important;
    }

    /* Override Streamlit's primary color everywhere */
    .stApp [data-testid="stMarkdownContainer"] a,
    .stApp .st-emotion-cache-nahz7x a {
        color: var(--accent) !important;
    }

    /* Metric cards - more compact */
    [data-testid="stMetric"] {
        background: transparent !important;
        padding: 0 !important;
    }
    [data-testid="stMetricLabel"] {
        font-size: 0.7rem !important;
    }

    /* Welcome section */
    .welcome-section {
        text-align: center;
        padding: 4rem 1rem 1rem;
    }
    .welcome-icon {
        width: 56px;
        height: 56px;
        margin: 0 auto 1.5rem;
        background: linear-gradient(135deg, #DA7756, #E8956F);
        border-radius: 16px;
        display: flex;
        align-items: center;
        justify-content: center;
        font-size: 1.6rem;
        box-shadow: 0 8px 32px rgba(218,119,86,0.25), 0 0 0 1px rgba(218,119,86,0.1);
    }
    .welcome-section h2 {
        font-size: 1.5rem;
        font-weight: 600;
        margin-bottom: 0.4rem;
        color: var(--text-primary);
        letter-spacing: -0.01em;
    }
    .welcome-section p {
        color: var(--text-secondary);
        font-size: 0.9rem;
        margin-bottom: 0;
    }
    .welcome-divider {
        width: 40px;
        height: 3px;
        background: linear-gradient(90deg, var(--accent), transparent);
        margin: 1.5rem auto 1.5rem;
        border-radius: 2px;
    }
    .example-label {
        font-size: 0.7rem;
        text-transform: uppercase;
        letter-spacing: 0.1em;
        color: var(--text-dim);
        text-align: center;
        margin-bottom: 1rem;
    }

    /* Sidebar section labels */
    .sidebar-label {
        font-size: 0.65rem;
        text-transform: uppercase;
        letter-spacing: 0.08em;
        color: var(--text-dim);
        margin-bottom: 0.3rem;
    }

    /* Sidebar brand header */
    .sidebar-brand {
        display: flex;
        align-items: center;
        gap: 10px;
        padding-bottom: 0.5rem;
        margin-bottom: 0.5rem;
        border-bottom: 1px solid rgba(218,119,86,0.15);
    }
    .sidebar-brand-icon {
        width: 32px;
        height: 32px;
        background: linear-gradient(135deg, #DA7756, #E8956F);
        border-radius: 8px;
        display: flex;
        align-items: center;
        justify-content: center;
        font-size: 1rem;
        flex-shrink: 0;
    }
    .sidebar-brand-text {
        font-size: 0.95rem;
        font-weight: 600;
        color: var(--text-primary);
        line-height: 1.2;
    }
    .sidebar-brand-sub {
        font-size: 0.65rem;
        color: var(--text-dim);
    }

    /* Sidebar stat pill */
    .stat-pill {
        display: inline-block;
        background: rgba(218,119,86,0.1);
        border: 1px solid rgba(218,119,86,0.15);
        border-radius: 12px;
        padding: 3px 10px;
        font-size: 0.7rem;
        color: var(--accent);
        margin-right: 4px;
    }

    /* Compact source list */
    .source-item {
        font-size: 0.8rem;
        color: var(--text-secondary);
        padding: 0.2rem 0;
        padding-left: 0.5rem;
        border-left: 2px solid rgba(218,119,86,0.2);
        margin-bottom: 0.15rem;
    }

    /* Response time badge */
    .time-badge {
        display: inline-block;
        background: rgba(218,119,86,0.08);
        border-radius: 10px;
        padding: 1px 8px;
        font-size: 0.72rem;
        color: var(--accent);
    }

    /* Chat input glow on focus */
    [data-testid="stChatInput"] {
        transition: box-shadow 0.3s ease;
    }
    [data-testid="stChatInput"]:focus-within {
        box-shadow: 0 0 0 1px rgba(218,119,86,0.3), 0 4px 16px rgba(218,119,86,0.08) !important;
    }

    /* Version footer */
    .version-footer {
        font-size: 0.65rem;
        color: var(--text-dim);
        text-align: center;
        padding-top: 0.5rem;
        border-top: 1px solid var(--border-subtle);
    }
</style>
""", unsafe_allow_html=True)


# ─── Example Questions ────────────────────────────────────────
EXAMPLE_QUESTIONS = [
    ("What are DLP policies?", "Power Platform"),
    ("How does unified routing work?", "Customer Service"),
    ("Explain prediction models", "Customer Insights"),
    ("What are posting groups?", "Business Central"),
    ("How does Key Vault manage secrets?", "Azure"),
    ("Compare environment types", "Power Platform"),
]

# ─── Prompt Template ───────────────────────────────────────────
RAG_PROMPT = ChatPromptTemplate.from_template("""
You are a senior Microsoft Dynamics 365, Power Platform, and Azure technical architect.
Answer using the provided context. For every design choice or recommendation:
- Explain WHY, not just what
- Note relevant trade-offs or alternatives
- Flag constraints or limitations from the documentation
Synthesize information across multiple context chunks into a cohesive answer.
If parts of the question aren't covered by the context, answer what you can
and clearly note which parts lack documentation.
Do not fabricate specific features or settings not mentioned in the context.
Use the conversation history to understand follow-up questions - if the user
says "tell me more" or "how does that work", refer back to the prior topic.

Context:
{context}

Conversation history:
{chat_history}

Question: {question}

Answer:""")

DEEP_PROMPT = ChatPromptTemplate.from_template("""
You are a principal-level Microsoft Dynamics 365, Power Platform, and Azure solution architect.
Provide an expert, structured analysis using the provided context. Your answer MUST include:

1. **Direct Answer** - Address the question with specifics from the documentation
2. **Architecture Reasoning** - Explain WHY this approach, not just what. Reference constraints.
3. **Trade-offs** - For each major choice, state what you chose, what the alternative was, and the cost of your choice
4. **Failure Modes & Risks** - What could go wrong, edge cases, operational concerns
5. **Implementation Considerations** - Practical details: sequencing, dependencies, gotchas

Synthesize across all provided context chunks. Connect concepts across different documentation sources.
If the context doesn't cover something, say so explicitly rather than guessing.
Do not fabricate features or settings not mentioned in the context.
Use the conversation history to understand follow-up questions.

Context:
{context}

Conversation history:
{chat_history}

Question: {question}

Answer:""")


# ─── Helper Functions ──────────────────────────────────────────

def format_docs(docs):
    """Combine retrieved documents into a single string for the prompt."""
    filtered = [
        doc for doc in docs
        if not doc.metadata.get("source", "").endswith("TOC.md")
        and "/includes/" not in doc.metadata.get("source", "")
    ]
    return "\n\n---\n\n".join(doc.page_content for doc in filtered)


def get_source_list(docs):
    """Extract unique source file paths from retrieved docs."""
    filtered = [
        doc for doc in docs
        if not doc.metadata.get("source", "").endswith("TOC.md")
        and "/includes/" not in doc.metadata.get("source", "")
    ]
    seen = set()
    sources = []
    for doc in filtered:
        src = doc.metadata.get("source", "unknown")
        if src not in seen:
            seen.add(src)
            sources.append(src)
    return sources


def format_source_path(raw_path):
    """Convert raw file paths to readable names."""
    path = raw_path.replace("docs/", "", 1)
    path = re.sub(r'\.md$', '', path)
    parts = path.split("/")
    cleaned = []
    for p in parts:
        if not cleaned or p != cleaned[-1]:
            cleaned.append(p)
    formatted = []
    for part in cleaned:
        words = part.replace("-", " ").replace("_", " ")
        formatted.append(words.title())
    return " / ".join(formatted)


def get_memory_turn_count():
    """Count conversation turns in memory."""
    if "memory" not in st.session_state:
        return 0
    history = st.session_state.memory.load_memory_variables({}).get("chat_history", "")
    if not history:
        return 0
    return history.count("Human:")


@st.cache_resource(show_spinner=False)
def load_retriever(k=10):
    """Build the hybrid retriever (vector + BM25). Cached after first run."""
    embeddings = get_embeddings()
    vectorstore = Chroma(
        persist_directory=CHROMA_DIR,
        embedding_function=embeddings,
    )
    vector_retriever = vectorstore.as_retriever(
        search_type="similarity",
        search_kwargs={"k": k * 2},
    )
    bm25_docs = []
    collection = vectorstore._collection
    total = collection.count()
    batch_size = 5000
    for offset in range(0, total, batch_size):
        batch = collection.get(
            include=["documents", "metadatas"],
            limit=batch_size,
            offset=offset,
        )
        for doc, meta in zip(batch["documents"], batch["metadatas"]):
            source = meta.get("source", "")
            if source.endswith("TOC.md") or "/includes/" in source:
                continue
            bm25_docs.append(Document(page_content=doc, metadata=meta))
    bm25_retriever = BM25Retriever.from_documents(bm25_docs, k=k)
    hybrid_retriever = EnsembleRetriever(
        retrievers=[vector_retriever, bm25_retriever],
        weights=[0.5, 0.5],
    )
    return hybrid_retriever, vectorstore, total


# ─── Initialize Session State ──────────────────────────────────

if "messages" not in st.session_state:
    st.session_state.messages = []

if "memory" not in st.session_state:
    st.session_state.memory = ConversationBufferWindowMemory(
        k=MEMORY_WINDOW,
        memory_key="chat_history",
        return_messages=False,
    )


# ─── Sidebar ───────────────────────────────────────────────────

with st.sidebar:
    st.markdown("""
    <div class="sidebar-brand">
        <div class="sidebar-brand-icon">📚</div>
        <div>
            <div class="sidebar-brand-text">RAG Knowledge Base</div>
            <div class="sidebar-brand-sub">Microsoft Docs Assistant</div>
        </div>
    </div>
    """, unsafe_allow_html=True)

    st.markdown("")

    # Provider
    st.markdown('<div class="sidebar-label">PROVIDER</div>', unsafe_allow_html=True)
    provider_display = "OpenAI" if PROVIDER == "openai" else "Ollama (Local)"
    st.markdown(f"**{provider_display}**")
    st.caption("Set in `.env`")

    st.markdown("")

    # Retrieval
    st.markdown('<div class="sidebar-label">RETRIEVAL</div>', unsafe_allow_html=True)
    deep_mode = st.toggle("Deep analysis", value=False, help="Structured reasoning with trade-offs, risks, and implementation details")
    rerank_on = st.toggle("Re-rank results", value=RERANKER_ENABLED, help="Score and re-order chunks by relevance (improves quality)")
    show_sources = st.toggle("Show sources", value=True)
    num_results = st.slider("Chunks", min_value=3, max_value=20, value=10)

    st.markdown("")

    # Memory
    st.markdown('<div class="sidebar-label">MEMORY</div>', unsafe_allow_html=True)
    turns = get_memory_turn_count()
    st.progress(min(turns / MEMORY_WINDOW, 1.0))
    st.caption(f"{turns}/{MEMORY_WINDOW} turns")

    if st.button("Clear Conversation", use_container_width=True, type="primary"):
        st.session_state.messages = []
        if "memory" in st.session_state:
            st.session_state.memory.clear()
        st.rerun()

    st.markdown("")

    # Stats (compact pills)
    if "db_stats" in st.session_state:
        st.markdown('<div class="sidebar-label">KNOWLEDGE BASE</div>', unsafe_allow_html=True)
        chunks_k = f"{st.session_state.db_stats / 1000:.0f}K"
        rerank_pill = '<span class="stat-pill">Re-rank ON</span>' if rerank_on else ''
        st.markdown(f"""
        <div style="margin-top: 0.3rem;">
            <span class="stat-pill">{chunks_k} chunks</span>
            <span class="stat-pill">{CHUNK_SIZE} chars</span>
            {rerank_pill}
        </div>
        """, unsafe_allow_html=True)

    # Version footer
    st.markdown("")
    st.markdown("")
    st.markdown('<div class="version-footer">v1.1 · Hybrid RAG + Re-Ranker</div>', unsafe_allow_html=True)


# ─── Main Chat Area ───────────────────────────────────────────

# Load retriever (cached) - over-fetch when re-ranking is enabled
fetch_k = RERANKER_CANDIDATES if rerank_on else num_results
if "retriever_loaded" not in st.session_state:
    with st.spinner("Loading knowledge base..."):
        base_retriever, vectorstore, total_chunks = load_retriever(k=fetch_k)
        st.session_state.db_stats = total_chunks
        st.session_state.retriever_loaded = True
else:
    base_retriever, vectorstore, total_chunks = load_retriever(k=fetch_k)
    st.session_state.db_stats = total_chunks

# Wrap with re-ranker if enabled
if rerank_on:
    compressor = get_reranker()
    if compressor is not None:
        compressor.top_n = num_results  # match the user's slider
        retriever = ContextualCompressionRetriever(
            base_compressor=compressor,
            base_retriever=base_retriever,
        )
    else:
        retriever = base_retriever
else:
    retriever = base_retriever

# Build the RAG chain - swap prompt based on deep analysis mode
llm = get_llm()
active_prompt = DEEP_PROMPT if deep_mode else RAG_PROMPT

# In deep mode, retrieve more chunks for richer context
if deep_mode:
    deep_base = EnsembleRetriever(
        retrievers=[
            vectorstore.as_retriever(search_type="similarity", search_kwargs={"k": num_results * 4}),
            base_retriever.retrievers[1] if hasattr(base_retriever, 'retrievers') else base_retriever,
        ],
        weights=[0.5, 0.5],
    )
    if rerank_on and compressor is not None:
        retriever_for_chain = ContextualCompressionRetriever(
            base_compressor=compressor,
            base_retriever=deep_base,
        )
    else:
        retriever_for_chain = deep_base
else:
    retriever_for_chain = retriever

rag_chain = (
    {
        "context": RunnableLambda(lambda x: x["question"]) | retriever_for_chain | format_docs,
        "chat_history": RunnableLambda(lambda x: x["chat_history"]),
        "question": RunnableLambda(lambda x: x["question"]),
    }
    | active_prompt
    | llm
    | StrOutputParser()
)

# Display chat history
for msg in st.session_state.messages:
    with st.chat_message(msg["role"]):
        st.markdown(msg["content"])
        if msg["role"] == "assistant":
            if "elapsed" in msg:
                st.markdown(f"<span class='time-badge'>{msg['elapsed']:.1f}s</span>", unsafe_allow_html=True)
            if show_sources and "sources" in msg and msg["sources"]:
                with st.expander(f"{len(msg['sources'])} sources"):
                    for src in msg["sources"]:
                        st.markdown(f"<div class='source-item'>📄 {format_source_path(src)}</div>", unsafe_allow_html=True)

# Welcome state
if not st.session_state.messages:
    st.markdown("""
    <div class="welcome-section">
        <div class="welcome-icon">📚</div>
        <h2>What can I help you find?</h2>
        <p>Search across Dynamics 365, Power Platform & Azure docs</p>
        <div class="welcome-divider"></div>
    </div>
    <div class="example-label">Try one of these</div>
    """, unsafe_allow_html=True)

    # Example questions as pill buttons in 3 columns
    cols = st.columns(3)
    for i, (question, topic) in enumerate(EXAMPLE_QUESTIONS):
        with cols[i % 3]:
            if st.button(question, key=f"ex_{i}", use_container_width=True, help=topic):
                st.session_state.pending_question = question
                st.rerun()


# ─── Handle Questions ─────────────────────────────────────────

def handle_question(question):
    """Process a question through the RAG chain and display results."""
    st.session_state.messages.append({"role": "user", "content": question})
    with st.chat_message("user"):
        st.markdown(question)

    with st.chat_message("assistant"):
        chunks_k = f"{st.session_state.get('db_stats', 0) / 1000:.0f}K"
        with st.spinner(f"Searching {chunks_k} docs..."):
            try:
                start = time.time()
                chat_history = st.session_state.memory.load_memory_variables({}).get(
                    "chat_history", ""
                )
                answer = rag_chain.invoke({
                    "question": question,
                    "chat_history": chat_history,
                })
                retrieved_docs = retriever.invoke(question)
                sources = get_source_list(retrieved_docs)
                elapsed = time.time() - start

                st.markdown(answer)
                st.markdown(f"<span class='time-badge'>{elapsed:.1f}s</span>", unsafe_allow_html=True)

                if show_sources and sources:
                    with st.expander(f"{len(sources)} sources"):
                        for src in sources:
                            st.markdown(f"<div class='source-item'>📄 {format_source_path(src)}</div>", unsafe_allow_html=True)

                st.session_state.memory.save_context(
                    {"input": question}, {"output": answer}
                )
                st.session_state.messages.append({
                    "role": "assistant",
                    "content": answer,
                    "sources": sources,
                    "elapsed": elapsed,
                })
            except Exception as e:
                st.error(f"Something went wrong: {str(e)[:200]}")
                st.caption("Check your API key and provider settings in `.env`")


# Handle pending question from example button
if "pending_question" in st.session_state:
    question = st.session_state.pending_question
    del st.session_state.pending_question
    handle_question(question)

# Chat input
if question := st.chat_input("Ask a question..."):
    handle_question(question)
