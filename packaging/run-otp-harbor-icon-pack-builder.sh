#!/bin/sh

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
"$script_directory/OtpHarbor.IconPackBuilder" "$@"
exit_code=$?

if [ -t 0 ]; then
    printf '\nPress Enter to close...'
    read -r _
fi

exit "$exit_code"
