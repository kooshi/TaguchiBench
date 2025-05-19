#!/usr/bin/env bash
# Script to run the TaguchiBench Engine

# --- Environment Setup ---
# Check for virtual environment and activate if it exists
if [ -f ./venv/bin/activate ]; then
  echo "Activating Python virtual environment from ./venv/bin/activate"
  source ./venv/bin/activate
else
  echo "WARNING: Virtual environment not found at ./venv/bin/activate"
  echo "         LiveBench may not function correctly if its dependencies are not installed."
  echo "         Please run './install-livebench.sh' first to set up the environment."
  echo "         Continuing anyway..."
  echo ""
fi

# --- Configuration ---
# Relative path to the TaguchiBench.Engine project from the script's location (repo root)
ENGINE_PROJECT_PATH="src/TaguchiBench.Engine" # If running with dotnet run
# ENGINE_EXECUTABLE_PATH="./src/TaguchiBench.Engine/bin/Release/netX.X/TaguchiBench.Engine" # Example if compiled

# Default configuration file name
DEFAULT_CONFIG_FILE="config.yaml" # User provides this, or simple-livebench-config.yaml

# --- Script Variables ---
CONFIG_FILE=""
RECOVER_STATE_FILE=""
REPORT_ONLY_HTML_FROM_STATE_FILE=""
REPORT_ONLY_MARKDOWN_FROM_STATE_FILE=""
OUTPUT_DIR_OVERRIDE=""
SHOW_HELP=false

# --- Helper Functions ---
print_usage() {
  echo "Usage: $0 [options]"
  echo ""
  echo "Runs the TaguchiBench Engine for experiment design, execution, and analysis."
  echo ""
  echo "Options:"
  echo "  -c, --config <file>        Path to the YAML engine configuration file."
  echo "                             (Default: '$DEFAULT_CONFIG_FILE' if it exists in current dir, or a project-specific sample)"
  echo "  -r, --recover <statefile>  Path to a .yaml experiment state file to recover and continue."
  echo "                             Cannot be used with --config."
  echo "  --report-html <statefile>  Generate only an HTML report from the given .yaml experiment state file."
  echo "                             Outputs to 'outputDirectory' in state file or one specified by --output-dir."
  echo "                             Cannot be used with --config or --recover."
  echo "  --report-md <statefile>    Generate only a Markdown report from the given .yaml experiment state file."
  echo "                             Outputs to 'outputDirectory' in state file or one specified by --output-dir."
  echo "                             Cannot be used with --config or --recover."
  echo "  -o, --output-dir <dir>     Override the output directory specified in the config/state file."
  echo "                             Applies to normal runs, recovery, and report-only modes."
  echo "  -v, --verbose              Enable verbose logging for the Engine (overrides config)."
  echo "  -h, --help                 Show this help message and exit."
  echo ""
  echo "Examples:"
  echo "  $0 --config my_experiment_config.yaml"
  echo "  $0 --config simple-livebench-config.yaml"
  echo "  $0 --recover ./taguchi_experiment_results/state_some_target_after_oa_run_X_timestamp.yaml -o ./recovered_results"
  echo "  $0 --report-html ./taguchi_experiment_results/state_some_target_experiment_completed_timestamp.yaml"
}

# --- Argument Parsing ---
while [[ $# -gt 0 ]]; do
  case $1 in
    -c|--config)
      CONFIG_FILE="$2"
      shift 2
      ;;
    -r|--recover)
      RECOVER_STATE_FILE="$2"
      shift 2
      ;;
    --report-html)
      REPORT_ONLY_HTML_FROM_STATE_FILE="$2"
      shift 2
      ;;
    --report-md)
      REPORT_ONLY_MARKDOWN_FROM_STATE_FILE="$2"
      shift 2
      ;;
    -o|--output-dir)
      OUTPUT_DIR_OVERRIDE="$2"
      shift 2
      ;;
    -v|--verbose)
      ENGINE_ARGS+=(--verbose)
      shift
      ;;
    -h|--help)
      SHOW_HELP=true
      shift
      ;;
    *)
      echo "Unknown option: $1"
      print_usage
      exit 1
      ;;
  esac
done

if [ "$SHOW_HELP" = true ]; then
  print_usage
  exit 0
fi

# --- Validate Arguments ---
MODE_ARGS_COUNT=0
if [ -n "$CONFIG_FILE" ]; then ((MODE_ARGS_COUNT++)); fi
if [ -n "$RECOVER_STATE_FILE" ]; then ((MODE_ARGS_COUNT++)); fi
if [ -n "$REPORT_ONLY_HTML_FROM_STATE_FILE" ]; then ((MODE_ARGS_COUNT++)); fi
if [ -n "$REPORT_ONLY_MARKDOWN_FROM_STATE_FILE" ]; then ((MODE_ARGS_COUNT++)); fi

if [ "$MODE_ARGS_COUNT" -eq 0 ]; then
  if [ -f "./$DEFAULT_CONFIG_FILE" ]; then 
    CONFIG_FILE="./$DEFAULT_CONFIG_FILE"
    echo "No mode specified, defaulting to --config '$CONFIG_FILE'"
  elif [ -f "./simple-livebench-config.yaml" ]; then
    CONFIG_FILE="./simple-livebench-config.yaml"
    echo "No mode specified, defaulting to --config '$CONFIG_FILE'"
  else
    echo "Error: You must specify an operation mode: --config, --recover, --report-html, or --report-md."
    echo "Alternatively, ensure '$DEFAULT_CONFIG_FILE' or 'simple-livebench-config.yaml' exists in the current directory for default --config mode."
    print_usage
    exit 1
  fi
elif [ "$MODE_ARGS_COUNT" -gt 1 ]; then
  echo "Error: Options --config, --recover, --report-html, and --report-md are mutually exclusive."
  print_usage
  exit 1
fi

# --- Build Command for Engine ---
# ENGINE_ARGS array is already being populated by --verbose if present
# Add other arguments
if [ -n "$CONFIG_FILE" ]; then
  if [ ! -f "$CONFIG_FILE" ]; then
    echo "Error: Configuration file '$CONFIG_FILE' not found."
    exit 1
  fi
  ENGINE_ARGS+=(--config "$CONFIG_FILE")
elif [ -n "$RECOVER_STATE_FILE" ]; then
  if [ ! -f "$RECOVER_STATE_FILE" ]; then
    echo "Error: State file for recovery '$RECOVER_STATE_FILE' not found."
    exit 1
  fi
  ENGINE_ARGS+=(--recover "$RECOVER_STATE_FILE")
elif [ -n "$REPORT_ONLY_HTML_FROM_STATE_FILE" ]; then
  if [ ! -f "$REPORT_ONLY_HTML_FROM_STATE_FILE" ]; then
    echo "Error: State file for HTML report generation '$REPORT_ONLY_HTML_FROM_STATE_FILE' not found."
    exit 1
  fi
  ENGINE_ARGS+=(--report-html "$REPORT_ONLY_HTML_FROM_STATE_FILE")
elif [ -n "$REPORT_ONLY_MARKDOWN_FROM_STATE_FILE" ]; then
  if [ ! -f "$REPORT_ONLY_MARKDOWN_FROM_STATE_FILE" ]; then
    echo "Error: State file for Markdown report generation '$REPORT_ONLY_MARKDOWN_FROM_STATE_FILE' not found."
    exit 1
  fi
  ENGINE_ARGS+=(--report-md "$REPORT_ONLY_MARKDOWN_FROM_STATE_FILE")
fi

if [ -n "$OUTPUT_DIR_OVERRIDE" ]; then
  ENGINE_ARGS+=(--output-dir "$OUTPUT_DIR_OVERRIDE")
fi

# --- Check for dotnet and Engine Project ---
if ! command -v dotnet &> /dev/null; then
    echo "Error: dotnet SDK is required but not found in PATH."
    exit 1
fi

# Assuming script is run from repo root, and ENGINE_PROJECT_PATH is relative to that (e.g., "src/TaguchiBench.Engine")
if [ ! -d "$ENGINE_PROJECT_PATH" ]; then
    echo "Error: TaguchiBench Engine project path '$ENGINE_PROJECT_PATH' not found from $(pwd)."
    echo "Please check the ENGINE_PROJECT_PATH variable in this script or run from the repository root."
    exit 1
fi


# --- Execute ---
# Build step is good practice if running from source
echo "Building TaguchiBench.Engine..."
dotnet build "$ENGINE_PROJECT_PATH" -c Release # Or Debug, as appropriate

echo ""
echo "Running TaguchiBench Engine..."
echo "Command: dotnet run --project $ENGINE_PROJECT_PATH -- ${ENGINE_ARGS[@]}"

dotnet run --project "$ENGINE_PROJECT_PATH" -- "${ENGINE_ARGS[@]}"

echo ""
echo "TaguchiBench Engine run completed."