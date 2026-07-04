FROM python:3.11-slim

WORKDIR /app

# Install system dependencies for scientific Python
RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY backend/ ./backend/
COPY pyproject.toml .

# Create directories for model persistence
RUN mkdir -p backend/rl/models backend/pql backend/qml

EXPOSE 8000

CMD ["uvicorn", "backend.api.main:app", "--host", "0.0.0.0", "--port", "8000", "--workers", "2"]
