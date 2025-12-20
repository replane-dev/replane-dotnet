#!/bin/bash
set -e

# Get the latest version tag
LATEST_TAG=$(git describe --tags --abbrev=0 2>/dev/null || echo "v0.0.0")
echo "Current version: $LATEST_TAG"

# Parse version numbers
VERSION=${LATEST_TAG#v}
MAJOR=$(echo "$VERSION" | cut -d. -f1)
MINOR=$(echo "$VERSION" | cut -d. -f2)
PATCH=$(echo "$VERSION" | cut -d. -f3)

# Ask user what to bump
echo ""
echo "What do you want to bump?"
echo "  1) major ($MAJOR.$MINOR.$PATCH -> $((MAJOR + 1)).0.0)"
echo "  2) minor ($MAJOR.$MINOR.$PATCH -> $MAJOR.$((MINOR + 1)).0)"
echo "  3) patch ($MAJOR.$MINOR.$PATCH -> $MAJOR.$MINOR.$((PATCH + 1)))"
echo ""
read -p "Enter choice [1-3]: " CHOICE

case $CHOICE in
    1)
        MAJOR=$((MAJOR + 1))
        MINOR=0
        PATCH=0
        ;;
    2)
        MINOR=$((MINOR + 1))
        PATCH=0
        ;;
    3)
        PATCH=$((PATCH + 1))
        ;;
    *)
        echo "Invalid choice"
        exit 1
        ;;
esac

NEW_TAG="v$MAJOR.$MINOR.$PATCH"
echo ""
echo "New version: $NEW_TAG"
read -p "Create and push tag? [y/N]: " CONFIRM

if [[ "$CONFIRM" =~ ^[Yy]$ ]]; then
    git tag "$NEW_TAG"
    echo "Created tag $NEW_TAG"

    git push origin "$NEW_TAG"
    echo "Pushed tag $NEW_TAG to origin"

    echo ""
    echo "Done! Version $NEW_TAG has been tagged and pushed."
else
    echo "Aborted."
    exit 0
fi
