"""Windows AF_UNIX 的地址转换；收发、超时和句柄所有权仍交给 socket。"""

import ctypes
import os
import select
import socket
from ctypes import wintypes


_AF_UNIX = 1
_PATH_SIZE = 108
_kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
_ws2_32 = ctypes.WinDLL("ws2_32")
_ws2_32.connect.argtypes = [ctypes.c_size_t, ctypes.c_void_p, ctypes.c_int]
_ws2_32.connect.restype = ctypes.c_int
_ws2_32.WSAGetLastError.restype = ctypes.c_int


class _SockaddrUnix(ctypes.Structure):
    _fields_ = [("family", ctypes.c_ushort), ("path", ctypes.c_char * _PATH_SIZE)]


def _temp_path():
    # 与 Go os.TempDir 一样，优先采用系统提供的 GetTempPath2W。
    function = getattr(_kernel32, "GetTempPath2W", _kernel32.GetTempPathW)
    function.argtypes = [wintypes.DWORD, wintypes.LPWSTR]
    function.restype = wintypes.DWORD
    size = 261
    while True:
        buffer = ctypes.create_unicode_buffer(size)
        length = function(size, buffer)
        if not length:
            raise ctypes.WinError(ctypes.get_last_error())
        if length < size:
            return buffer.value
        size = length + 1


def default_socket():
    override = os.environ.get("HCU_SOCKET", "").strip()
    if override:
        return override
    directory = _temp_path()
    path = os.path.join(directory, "hcu--1", "desktop-default.sock")
    if len(path.encode("utf-8")) >= _PATH_SIZE:
        function = _kernel32.GetShortPathNameW
        function.argtypes = [wintypes.LPCWSTR, wintypes.LPWSTR, wintypes.DWORD]
        function.restype = wintypes.DWORD
        size = function(directory, None, 0)
        if size:
            buffer = ctypes.create_unicode_buffer(size)
            length = function(directory, buffer, size)
            if 0 < length < size:
                short = os.path.join(buffer.value, "hcu--1", "desktop-default.sock")
                if len(short.encode("utf-8")) < len(path.encode("utf-8")):
                    path = short
    return path


def connect(path, timeout):
    path = os.fsdecode(path)
    encoded = path.encode("utf-8")
    if not encoded or b"\0" in encoded or len(encoded) >= _PATH_SIZE:
        raise ValueError("Windows Unix socket path must contain 1 to 107 UTF-8 bytes and no NUL; "
                         "set HCU_SOCKET to a shorter path in a private directory")
    # CPython 没有暴露 AF_UNIX 地址转换，但 socket 支持该 Winsock 句柄。
    # 不自建收发循环，也不在这里重试任何业务请求。
    sock = socket.socket(_AF_UNIX, socket.SOCK_STREAM, 0)
    try:
        sock.setblocking(False)
        address = _SockaddrUnix(_AF_UNIX, encoded)
        if _ws2_32.connect(sock.fileno(), ctypes.byref(address), ctypes.sizeof(address)):
            error = _ws2_32.WSAGetLastError()
            if error not in (10035, 10036, 10037):
                raise ctypes.WinError(error)
            _, writable, exceptional = select.select([], [sock], [sock], timeout)
            if not writable and not exceptional:
                raise TimeoutError("timed out connecting to Windows daemon socket")
            error = sock.getsockopt(socket.SOL_SOCKET, socket.SO_ERROR)
            if error:
                raise ctypes.WinError(error)
        sock.settimeout(timeout)
        return sock
    except BaseException:
        sock.close()
        raise
