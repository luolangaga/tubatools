"""OSWorld V1/V2 harness for the hybrid task agent.

The harness imports ``desktop_env`` from the pinned OSWorld-V2 checkout
(``OSWORLD_V2_ROOT``), so importing this package puts that checkout on
``sys.path``.
"""

import sys

from hcu.paths import OSWORLD_ROOT

if str(OSWORLD_ROOT) not in sys.path:
    sys.path.insert(0, str(OSWORLD_ROOT))
