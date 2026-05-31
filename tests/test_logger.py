import pytest
import os
from pathlib import Path
from logger import Logger


class TestLogger:
    """Test suite for Logger class."""

    def test_logger_writes_to_file(self, tmp_path):
        """Test logger writes structured logs to file"""
        log_file = tmp_path / "test.log"
        logger = Logger(str(log_file))

        logger.info(table="transac", status="success", rows=100)

        assert log_file.exists()
        content = log_file.read_text()
        assert "transac" in content
        assert "success" in content
        assert "100" in content

    def test_logger_error_format(self, tmp_path):
        """Test logger formats errors correctly"""
        log_file = tmp_path / "test.log"
        logger = Logger(str(log_file))

        logger.error(table="transac", error_type="timeout", detail="Oracle unavailable")

        content = log_file.read_text()
        assert "ERROR" in content
        assert "timeout" in content
        assert "unavailable" in content

    def test_logger_includes_timestamp(self, tmp_path):
        """Test logger includes timestamp in each log"""
        log_file = tmp_path / "test.log"
        logger = Logger(str(log_file))

        logger.info(table="transac", status="success", rows=1)

        content = log_file.read_text()
        # Format should be [YYYY-MM-DD HH:MM:SS]
        assert "[" in content and "]" in content
