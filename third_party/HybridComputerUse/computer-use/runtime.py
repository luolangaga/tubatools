#!/usr/bin/env python3
"""Provider process entrypoint for the Linux implementation."""

from native.linux.provider.wire import main  # pyright: ignore[reportImplicitRelativeImport]

if __name__ == "__main__":
    main()
