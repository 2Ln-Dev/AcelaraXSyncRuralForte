"""
Configuration loader and validator for ERP Sync API.

Loads environment variables from .env file and validates required fields.
Handles type conversions and default values for optional fields.
"""

import os
from pathlib import Path
from dotenv import load_dotenv


class ConfigError(Exception):
    """Exception raised for configuration errors."""

    pass


class Config:
    """Configuration class for ERP Sync API.

    Loads environment variables from .env file and validates them.
    Handles type conversions for integer fields and provides defaults for optional fields.
    """

    # Required fields that must be present in the environment
    REQUIRED_FIELDS = [
        "ORACLE_HOST",
        "ORACLE_PORT",
        "ORACLE_SID",
        "ORACLE_USER",
        "ORACLE_PASSWORD",
        "SUPABASE_URL",
        "SUPABASE_KEY",
        "SYNC_INTERVAL_SECONDS",
        "CODI_TEN",
    ]

    # Integer fields that need to be converted from string to int
    INT_FIELDS = ["ORACLE_PORT", "SYNC_INTERVAL_SECONDS"]

    # Optional fields with their defaults
    OPTIONAL_FIELDS = {
        "LOG_FILE_PATH": "./sync.log",
        "BATCH_SIZE": 1000,
        "LOG_LEVEL": "INFO",
    }

    def __init__(self, env_path: str = ".env"):
        """Initialize Config by loading and validating environment variables.

        Args:
            env_path: Path to the .env file to load. Defaults to ".env".

        Raises:
            ConfigError: If required fields are missing or values are invalid.
        """
        self.env_path = env_path
        self._load_and_validate()

    def _load_and_validate(self):
        """Load environment variables from .env file and validate them.

        Raises:
            ConfigError: If required fields are missing or values are invalid.
        """
        # Load environment variables from .env file
        if os.path.exists(self.env_path):
            load_dotenv(self.env_path, override=True)
        else:
            raise ConfigError(f"Environment file not found: {self.env_path}")

        # Validate and load required fields
        for field in self.REQUIRED_FIELDS:
            value = os.getenv(field)
            if value is None:
                raise ConfigError(f"Required field missing: {field}")

            # Convert integer fields
            if field in self.INT_FIELDS:
                try:
                    value = int(value)
                except (ValueError, TypeError):
                    raise ConfigError(
                        f"Invalid value for {field}: expected integer, got '{os.getenv(field)}'"
                    )

            # Set attribute with lowercase name
            attr_name = field.lower()
            setattr(self, attr_name, value)

        # Load and validate optional fields
        for field, default in self.OPTIONAL_FIELDS.items():
            value = os.getenv(field)

            if value is None:
                # Use default value
                if field == "BATCH_SIZE":
                    value = default  # Already an int
                else:
                    value = default
            else:
                # Convert integer fields
                if field == "BATCH_SIZE":
                    try:
                        value = int(value)
                    except (ValueError, TypeError):
                        raise ConfigError(
                            f"Invalid value for {field}: expected integer, got '{os.getenv(field)}'"
                        )

            attr_name = field.lower()
            setattr(self, attr_name, value)

    def __repr__(self):
        """Return string representation of Config object."""
        fields = []
        for field in self.REQUIRED_FIELDS + list(self.OPTIONAL_FIELDS.keys()):
            attr_name = field.lower()
            value = getattr(self, attr_name, None)
            # Mask sensitive fields
            if "password" in attr_name.lower() or "key" in attr_name.lower():
                value = "***MASKED***"
            fields.append(f"{attr_name}={value}")
        return f"Config({', '.join(fields)})"
