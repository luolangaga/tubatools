"""Setup assets: local files first, then a URL-keyed download cache."""

import ast
import importlib.util
from pathlib import Path
import sys
from types import ModuleType, SimpleNamespace

import pytest
import requests

ROOT = Path(__file__).resolve().parents[1]
URL = "https://huggingface.co/datasets/xlangai/ubuntu_osworld_file_cache/resolve/main/calc/t/a%20b.xlsx"


class Response:
    def __init__(self, body=b"abcdef", length=None, status_error=None, stream_error=None):
        self.body = body
        self.headers = {"Content-Length": str(len(body) if length is None else length)}
        self.status_error = status_error
        self.stream_error = stream_error

    def __enter__(self):
        return self

    def __exit__(self, *args):
        pass

    def raise_for_status(self):
        if self.status_error:
            raise self.status_error

    def iter_content(self, chunk_size):
        yield self.body[:3]
        if self.stream_error:
            raise self.stream_error
        yield self.body[3:]


@pytest.fixture
def source(monkeypatch, tmp_path):
    """The real overlay module, without importing the VM dependency tree."""
    package = ModuleType("desktop_env")
    package.__path__ = [str(ROOT / "osworld-overlay/desktop_env")]
    monkeypatch.setitem(sys.modules, "desktop_env", package)
    spec = importlib.util.spec_from_file_location(
        "desktop_env.file_source", ROOT / "osworld-overlay/desktop_env/file_source.py"
    )
    module = importlib.util.module_from_spec(spec)
    monkeypatch.setitem(sys.modules, spec.name, module)
    spec.loader.exec_module(module)
    module.requests_made = []

    def get(url, **kwargs):
        module.requests_made.append(url)
        return module.next_response

    monkeypatch.setattr(requests, "get", get)
    module.next_response = Response()
    module.configure(tmp_path / "cache")
    return module


def test_cache_layout_is_keyed_by_host_and_decoded_path(source, tmp_path):
    assert source.cache_path(URL) == tmp_path / "cache/huggingface.co/datasets/xlangai/ubuntu_osworld_file_cache/resolve/main/calc/t/a b.xlsx"
    with_query = source.cache_path(source.HISTORY_DB_URL)
    assert with_query.name.startswith("history_empty.sqlite.")
    assert source.cache_path("/local/file") is None
    with pytest.raises(ValueError):
        source.cache_path("https://host/a/../../etc/passwd")


def test_missing_asset_is_downloaded_once_into_the_cache(source):
    first = source.require_setup_source(URL)
    assert Path(first).read_bytes() == b"abcdef"
    assert source.require_setup_source(URL) == first
    assert source.requests_made == [URL]


def test_offline_mode_never_downloads(source):
    source.configure(source._cache_dir, offline=True)
    with pytest.raises(FileNotFoundError, match="offline mode forbids downloads"):
        source.require_setup_source(URL)
    assert source.requests_made == []
    cached = source.cache_path(URL)
    cached.parent.mkdir(parents=True)
    cached.write_bytes(b"prepared")
    assert Path(source.require_setup_source(URL)).read_bytes() == b"prepared"


def test_local_paths_and_file_uris_are_used_directly(source, tmp_path):
    path = tmp_path / "a b.txt"
    path.write_text("content")
    assert source.require_setup_source(path.as_uri()) == str(path)
    assert source.require_setup_source(str(path).replace(" ", "%20")) == str(path)
    with pytest.raises(FileNotFoundError):
        source.require_setup_source(str(tmp_path / "missing"))
    with pytest.raises(ValueError, match="must be local"):
        source.resolve_local_source("file://remote/path")
    assert source.requests_made == []


@pytest.mark.parametrize("response", [
    Response(length=100),
    Response(status_error=requests.HTTPError("503")),
    Response(stream_error=requests.ConnectionError("interrupted")),
])
def test_failed_download_publishes_nothing(source, response):
    source.next_response = response
    with pytest.raises((ValueError, requests.RequestException)):
        source.require_setup_source(URL)
    cached = source.cache_path(URL)
    assert not cached.exists()
    assert list(cached.parent.iterdir()) == []


def test_upstream_evaluators_resolve_cached_files_without_network(source):
    assert source.resolve_local_source(URL) is None
    source.require_setup_source(URL)
    assert source.resolve_local_source(URL) == str(source.cache_path(URL))
    assert source.requests_made == [URL]


def controller_method(name, source, **extra):
    tree = ast.parse((ROOT / "osworld-overlay/desktop_env/controllers/setup.py").read_text())
    controller = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == "SetupController")
    method = next(node for node in controller.body if isinstance(node, ast.FunctionDef) and node.name == name)
    code = ast.Module(body=[ast.ImportFrom(module="__future__", names=[ast.alias(name="annotations")], level=0), method], type_ignores=[])
    ast.fix_missing_locations(code)
    namespace = {
        "require_setup_source": source.require_setup_source,
        "HISTORY_DB_URL": source.HISTORY_DB_URL,
        **extra,
    }
    exec(compile(code, "setup.py", "exec"), namespace)
    return namespace[name]


def test_setup_download_resolves_the_whole_batch_before_upload(source, tmp_path):
    source.configure(source._cache_dir, offline=True)
    file = tmp_path / "input"
    file.write_bytes(b"new bytes")
    uploaded = []
    controller = SimpleNamespace(_upload_file_setup=uploaded.extend)
    download = controller_method("_download_setup", source)
    files = [{"url": str(file), "path": "/tmp/output"}, {"url": URL, "path": "/tmp/missing"}]
    with pytest.raises(FileNotFoundError):
        download(controller, files)
    assert uploaded == []
    download(controller, files[:1])
    assert uploaded == [{"local_path": str(file), "path": "/tmp/output"}]


def test_cloud_upload_sources_are_checked_before_auth_or_mutation(source):
    source.configure(source._cache_dir, offline=True)
    authenticate = lambda *args, **kwargs: pytest.fail("authentication before asset check")
    upload = controller_method("_googledrive_setup", source, GoogleAuth=authenticate)
    with pytest.raises(FileNotFoundError):
        upload(SimpleNamespace(), operation=["delete", "upload"], args=[
            {"query": "existing files"}, {"url": URL, "path": ["file"]},
        ])
