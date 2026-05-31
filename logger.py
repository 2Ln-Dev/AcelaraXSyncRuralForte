import os
from datetime import datetime


class Logger:
    """Structured file-based logger for ERP sync API."""

    def __init__(self, log_file_path):
        """Initialize logger with file path.

        Args:
            log_file_path: Path where log file will be written.
                          Parent directories are created if needed.
        """
        self.log_file_path = log_file_path
        # Create parent directories if needed
        os.makedirs(
            os.path.dirname(log_file_path) if os.path.dirname(log_file_path) else ".",
            exist_ok=True
        )

    def _write_log(self, **kwargs):
        """Write a structured log line to file.

        Builds log line as: [YYYY-MM-DD HH:MM:SS] KEY:value KEY:value ...

        Args:
            **kwargs: Key-value pairs to log
        """
        timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        parts = [f"[{timestamp}]"]

        for key, value in kwargs.items():
            parts.append(f"{key.upper()}:{value}")

        log_line = " ".join(parts)

        with open(self.log_file_path, 'a') as f:
            f.write(log_line + "\n")

    def info(self, **kwargs):
        """Log info level message.

        Args:
            **kwargs: Key-value pairs to log
        """
        self._write_log(**kwargs)

    def error(self, **kwargs):
        """Log error level message.

        Args:
            **kwargs: Key-value pairs to log (level=ERROR added automatically)
        """
        self._write_log(level="ERROR", **kwargs)

    def summary(self, **kwargs):
        """Log summary level message.

        Args:
            **kwargs: Key-value pairs to log (level=SUMMARY added automatically)
        """
        self._write_log(level="SUMMARY", **kwargs)
