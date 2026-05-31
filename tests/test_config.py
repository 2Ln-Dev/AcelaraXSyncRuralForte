import pytest
import os
from pathlib import Path
from config import Config, ConfigError


class TestConfig:
    """Test suite for Config class."""

    @pytest.fixture
    def env_file(self, tmp_path):
        """Create a temporary .env file for testing."""
        env_content = """ORACLE_HOST=localhost
ORACLE_PORT=1521
ORACLE_SID=ORCL
ORACLE_USER=testuser
ORACLE_PASSWORD=testpass
SUPABASE_URL=https://test.supabase.co
SUPABASE_KEY=test_key_123
SYNC_INTERVAL_SECONDS=3600
LOG_FILE_PATH=./test.log
BATCH_SIZE=1000
LOG_LEVEL=INFO
"""
        env_file = tmp_path / ".env"
        env_file.write_text(env_content)
        return env_file

    def test_load_config_from_env(self, env_file, monkeypatch):
        """Test that Config loads valid .env file and parses values correctly."""
        # Change to the directory with the .env file
        monkeypatch.chdir(env_file.parent)

        config = Config(env_path=str(env_file))

        assert config.oracle_host == "localhost"
        assert config.oracle_port == 1521
        assert isinstance(config.oracle_port, int)
        assert config.oracle_sid == "ORCL"
        assert config.oracle_user == "testuser"
        assert config.oracle_password == "testpass"
        assert config.supabase_url == "https://test.supabase.co"
        assert config.supabase_key == "test_key_123"
        assert config.sync_interval_seconds == 3600
        assert isinstance(config.sync_interval_seconds, int)
        assert config.log_file_path == "./test.log"
        assert config.batch_size == 1000
        assert config.log_level == "INFO"

    def test_config_missing_required_field(self, tmp_path, monkeypatch):
        """Test that missing required field raises ConfigError."""
        env_content = """ORACLE_HOST=localhost
ORACLE_SID=ORCL
ORACLE_USER=testuser
ORACLE_PASSWORD=testpass
SUPABASE_URL=https://test.supabase.co
SUPABASE_KEY=test_key_123
SYNC_INTERVAL_SECONDS=3600
LOG_FILE_PATH=./test.log
"""
        # Missing ORACLE_PORT intentionally to trigger error
        env_file = tmp_path / ".env"
        env_file.write_text(env_content)
        monkeypatch.chdir(env_file.parent)

        # Clear environment variables that might be set
        monkeypatch.delenv("ORACLE_PORT", raising=False)

        with pytest.raises(ConfigError) as exc_info:
            Config(env_path=str(env_file))

        assert "ORACLE_PORT" in str(exc_info.value)

    def test_config_invalid_port(self, tmp_path, monkeypatch):
        """Test that invalid port type raises ConfigError."""
        env_content = """ORACLE_HOST=localhost
ORACLE_PORT=invalid_port
ORACLE_SID=ORCL
ORACLE_USER=testuser
ORACLE_PASSWORD=testpass
SUPABASE_URL=https://test.supabase.co
SUPABASE_KEY=test_key_123
SYNC_INTERVAL_SECONDS=3600
LOG_FILE_PATH=./test.log
"""
        env_file = tmp_path / ".env"
        env_file.write_text(env_content)
        monkeypatch.chdir(env_file.parent)

        with pytest.raises(ConfigError) as exc_info:
            Config(env_path=str(env_file))

        assert "ORACLE_PORT" in str(exc_info.value) or "integer" in str(exc_info.value).lower()

    def test_config_invalid_sync_interval(self, tmp_path, monkeypatch):
        """Test that invalid sync interval type raises ConfigError."""
        env_content = """ORACLE_HOST=localhost
ORACLE_PORT=1521
ORACLE_SID=ORCL
ORACLE_USER=testuser
ORACLE_PASSWORD=testpass
SUPABASE_URL=https://test.supabase.co
SUPABASE_KEY=test_key_123
SYNC_INTERVAL_SECONDS=not_a_number
LOG_FILE_PATH=./test.log
"""
        env_file = tmp_path / ".env"
        env_file.write_text(env_content)
        monkeypatch.chdir(env_file.parent)

        with pytest.raises(ConfigError) as exc_info:
            Config(env_path=str(env_file))

        assert "SYNC_INTERVAL_SECONDS" in str(exc_info.value) or "integer" in str(exc_info.value).lower()

    def test_config_optional_fields_with_defaults(self, tmp_path, monkeypatch):
        """Test that optional fields use defaults when not provided."""
        env_content = """ORACLE_HOST=localhost
ORACLE_PORT=1521
ORACLE_SID=ORCL
ORACLE_USER=testuser
ORACLE_PASSWORD=testpass
SUPABASE_URL=https://test.supabase.co
SUPABASE_KEY=test_key_123
SYNC_INTERVAL_SECONDS=3600
"""
        # Omit optional fields: LOG_FILE_PATH, BATCH_SIZE, LOG_LEVEL
        env_file = tmp_path / ".env"
        env_file.write_text(env_content)
        monkeypatch.chdir(env_file.parent)

        config = Config(env_path=str(env_file))

        # Optional fields should have defaults or be None
        assert config.batch_size == 1000
        assert config.log_level == "INFO"
        assert config.log_file_path is not None  # Should have a default
