#!/bin/sh
set -e

# Find out the current active user ID running this container
CURRENT_UID=$(id -u)

echo "Container starting as UID: $CURRENT_UID"

# If running as root, we can chown the folder to whoever the user wants.
# If running as a custom user, we ensure the group permission bit takes care of it.
if [ "$CURRENT_UID" = "0" ]; then
    chown -R $CURRENT_UID:$CURRENT_UID /app/wwwroot
fi

# Fix permissions dynamically on boot
chmod -R 775 /app/wwwroot

# Execute the original .NET application entry point
exec dotnet Jellystat-API-Core.dll "$@"
