#!/bin/bash
set -e  # Exit on error

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
CYAN='\033[0;36m'
NC='\033[0m'

echo -e "${GREEN}Building Fetch Entities Advanced Module${NC}"

echo -e "${YELLOW}Compiling the project...${NC}"
dotnet build build.proj

if [ $? -ne 0 ]; then
    echo -e "${RED}Build failed!${NC}"
    exit 1
fi

echo -e "${YELLOW}Creating Decisions module package...${NC}"
dotnet msbuild build.proj -t:build_module

if [ $? -ne 0 ]; then
    echo -e "${RED}Module packaging failed!${NC}"
    exit 1
fi

echo -e "${GREEN}Module built successfully!${NC}"
echo -e "${CYAN}Output: Decisions.FetchEntitiesAdvanced.zip${NC}"
