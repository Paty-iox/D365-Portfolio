"""
Fetch Microsoft documentation from GitHub.

Microsoft publishes all their docs as Markdown files on GitHub.
This script downloads specific sections you care about.

Usage:
    python fetch_docs.py                      # download all configured doc sets
    python fetch_docs.py power-platform       # download only Power Platform docs
    python fetch_docs.py power-apps           # download only Power Apps docs
    python fetch_docs.py power-automate       # download only Power Automate docs
    python fetch_docs.py power-bi             # download only Power BI docs
    python fetch_docs.py customer-service     # download only Customer Service docs
    python fetch_docs.py sales                # download only Sales docs
    python fetch_docs.py customer-insights    # download only Customer Insights docs
    python fetch_docs.py business-central     # download only Business Central docs
    python fetch_docs.py azure                # download only Azure docs

After fetching, run 'python ingest.py' to embed them.
"""

import os
import sys
import subprocess
import shutil

DOCS_DIR = "docs"

# Each entry defines:
#   - repo: the GitHub repo to clone
#   - sparse_paths: specific folders to download (not the whole repo!)
#   - target: where to put them in your docs/ folder
DOC_SOURCES = {
    # ── POWER PLATFORM (all areas) ──────────────────────────────────
    "power-platform": {
        "repo": "https://github.com/MicrosoftDocs/power-platform.git",
        "sparse_paths": [
            "power-platform/admin",
            "power-platform/guidance",
            "power-platform/developer",
            "power-platform/alm",
        ],
        "target": os.path.join(DOCS_DIR, "power-platform"),
    },
    "power-apps": {
        "repo": "https://github.com/MicrosoftDocs/powerapps-docs.git",
        "sparse_paths": [
            "powerapps-docs/developer",
            "powerapps-docs/maker",
            "powerapps-docs/guidance",
        ],
        "target": os.path.join(DOCS_DIR, "power-apps"),
    },
    "power-automate": {
        "repo": "https://github.com/MicrosoftDocs/power-automate-docs.git",
        "sparse_paths": [
            "articles",
        ],
        "target": os.path.join(DOCS_DIR, "power-automate"),
    },
    "power-bi": {
        "repo": "https://github.com/MicrosoftDocs/powerbi-docs.git",
        "sparse_paths": [
            "powerbi-docs/guidance",
            "powerbi-docs/developer",
            "powerbi-docs/connect-data",
            "powerbi-docs/transform-model",
        ],
        "target": os.path.join(DOCS_DIR, "power-bi"),
    },

    # ── DYNAMICS 365 ────────────────────────────────────────────────
    "customer-service": {
        "repo": "https://github.com/MicrosoftDocs/dynamics-365-customer-engagement.git",
        "sparse_paths": [
            "ce/customer-service",
        ],
        "target": os.path.join(DOCS_DIR, "customer-service"),
    },
    "sales": {
        "repo": "https://github.com/MicrosoftDocs/dynamics-365-customer-engagement.git",
        "sparse_paths": [
            "ce/sales",
        ],
        "target": os.path.join(DOCS_DIR, "sales"),
    },
    "customer-insights": {
        "repo": "https://github.com/MicrosoftDocs/customer-insights.git",
        "sparse_paths": [
            "ci-docs",
        ],
        "target": os.path.join(DOCS_DIR, "customer-insights"),
    },
    "business-central": {
        "repo": "https://github.com/MicrosoftDocs/dynamics365smb-docs.git",
        "sparse_paths": [
            "business-central",
        ],
        "target": os.path.join(DOCS_DIR, "business-central"),
    },

    # ── AZURE (top areas for Power Platform architects) ─────────────
    # Topics still in azure-docs repo:
    "azure": {
        "repo": "https://github.com/MicrosoftDocs/azure-docs.git",
        "sparse_paths": [
            "articles/logic-apps",
            "articles/azure-functions",
            "articles/api-management",
            "articles/service-bus-messaging",
            "articles/event-grid",
            "articles/data-factory",
            "articles/storage",
            "articles/virtual-network",
            "articles/app-service",
        ],
        "target": os.path.join(DOCS_DIR, "azure"),
    },
    # Topics that moved to their own repos:
    "azure-entra-id": {
        "repo": "https://github.com/MicrosoftDocs/entra-docs.git",
        "sparse_paths": ["docs"],
        "target": os.path.join(DOCS_DIR, "azure", "entra-id"),
    },
    "azure-sql": {
        "repo": "https://github.com/MicrosoftDocs/sql-docs.git",
        "sparse_paths": ["azure-sql"],
        "target": os.path.join(DOCS_DIR, "azure", "azure-sql"),
    },
    "azure-cosmos-db": {
        "repo": "https://github.com/MicrosoftDocs/azure-databases-docs.git",
        "sparse_paths": ["articles/cosmos-db"],
        "target": os.path.join(DOCS_DIR, "azure", "cosmos-db"),
    },
    "azure-key-vault": {
        "repo": "https://github.com/MicrosoftDocs/azure-security-docs.git",
        "sparse_paths": ["articles/key-vault"],
        "target": os.path.join(DOCS_DIR, "azure", "key-vault"),
    },
    "azure-monitor": {
        "repo": "https://github.com/MicrosoftDocs/azure-monitor-docs.git",
        "sparse_paths": ["articles/azure-monitor"],
        "target": os.path.join(DOCS_DIR, "azure", "azure-monitor"),
    },
    "azure-devops": {
        "repo": "https://github.com/MicrosoftDocs/azure-devops-docs.git",
        "sparse_paths": ["docs"],
        "target": os.path.join(DOCS_DIR, "azure", "devops"),
    },
    "azure-ai": {
        "repo": "https://github.com/MicrosoftDocs/azure-ai-docs.git",
        "sparse_paths": ["articles/ai-services"],
        "target": os.path.join(DOCS_DIR, "azure", "ai-services"),
    },

    # ── DYNAMICS 365 INTEGRATION (dual-write, virtual tables) ────────
    "dual-write": {
        "repo": "https://github.com/MicrosoftDocs/dynamics-365-unified-operations-public.git",
        "sparse_paths": [
            "articles/fin-ops-core/dev-itpro/data-entities/dual-write",
        ],
        "target": os.path.join(DOCS_DIR, "dual-write"),
    },
}


def fetch_docs(name, source):
    """Use git sparse checkout to download only the folders we need."""
    repo = source["repo"]
    target = source["target"]
    sparse_paths = source["sparse_paths"]
    temp_dir = f"_temp_{name}"

    print(f"\n{'='*50}")
    print(f"📥 Fetching {name} docs...")
    print(f"   Repo: {repo}")
    print(f"   Folders: {', '.join(sparse_paths)}")
    print(f"{'='*50}")

    # Clean up any previous temp directory
    if os.path.exists(temp_dir):
        shutil.rmtree(temp_dir)

    try:
        # 1. Clone with no checkout (just get the repo structure)
        subprocess.run(
            ["git", "clone", "--no-checkout", "--depth", "1", "--filter=blob:none", repo, temp_dir],
            check=True,
        )

        # 2. Set up sparse checkout (only download specific folders)
        subprocess.run(
            ["git", "-C", temp_dir, "sparse-checkout", "init", "--cone"],
            check=True,
        )
        subprocess.run(
            ["git", "-C", temp_dir, "sparse-checkout", "set"] + sparse_paths,
            check=True,
        )

        # 3. Checkout the files
        subprocess.run(
            ["git", "-C", temp_dir, "checkout"],
            check=True,
        )

        # 4. Copy the markdown files to our docs folder
        os.makedirs(target, exist_ok=True)
        file_count = 0
        for sparse_path in sparse_paths:
            src = os.path.join(temp_dir, sparse_path)
            if not os.path.exists(src):
                print(f"   ⚠️  Path not found: {sparse_path}")
                continue

            for root, dirs, files in os.walk(src):
                for file in files:
                    if file.endswith((".md", ".txt")):
                        src_file = os.path.join(root, file)
                        # Preserve some folder structure
                        rel = os.path.relpath(src_file, temp_dir)
                        dst_file = os.path.join(target, rel)
                        os.makedirs(os.path.dirname(dst_file), exist_ok=True)
                        shutil.copy2(src_file, dst_file)
                        file_count += 1

        print(f"   ✅ Copied {file_count} files to {target}/")

    finally:
        # Clean up temp clone
        if os.path.exists(temp_dir):
            shutil.rmtree(temp_dir)


def main():
    # Determine which doc sets to fetch
    if len(sys.argv) > 1:
        requested = sys.argv[1:]
        sources = {}
        for name in requested:
            if name in DOC_SOURCES:
                sources[name] = DOC_SOURCES[name]
            else:
                print(f"❌ Unknown doc set: '{name}'")
                print(f"   Available: {', '.join(DOC_SOURCES.keys())}")
                return
    else:
        sources = DOC_SOURCES

    print("📚 Microsoft Docs Fetcher")
    print(f"   Fetching: {', '.join(sources.keys())}")
    print()
    print("⚠️  This will download docs from GitHub.")
    print("   Some repos are large - this may take a few minutes.")

    for name, source in sources.items():
        fetch_docs(name, source)

    print()
    print("=" * 50)
    print("✅ All done! Now run 'python ingest.py' to embed the docs.")
    print("=" * 50)


if __name__ == "__main__":
    main()
