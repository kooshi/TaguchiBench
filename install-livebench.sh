#!/usr/bin/env bash
# Script to install LiveBench and set up the environment

# Exit on error
set -e

# Check for Python
if ! command -v python3 &> /dev/null; then
    echo "Error: Python 3 is required but not installed. Please install Python 3 first."
    exit 1
fi

# Clone LiveBench repository
if [ ! -d "livebench" ]; then
    echo "Cloning LiveBench repository..."
    git clone -b system-prompt https://github.com/kooshi/LiveBench.git livebench
else
    echo "LiveBench directory already exists. Updating repository..."
    cd livebench
    git pull
    cd ..
fi

# Create and activate virtual environment
if [ ! -d "venv" ]; then
    echo "Creating virtual environment..."
    python3 -m venv venv
fi

echo "Activating virtual environment..."
source venv/bin/activate

# Install LiveBench
echo "Installing LiveBench..."
cd livebench
pip install -e .

echo "Downloading Data..."
cd livebench
python download_questions.py
# Livebench run fails without these directories. This is asinine.
for dir in data/live_bench/*/*/; do mkdir -p "${dir}model_answer"; done

cd ../..
echo "LiveBench installation complete!"
