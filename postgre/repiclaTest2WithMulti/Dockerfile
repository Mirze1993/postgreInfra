FROM postgres:18
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        python3 python3-pip \
        pgbackrest \
        curl && \
    pip install --break-system-packages "patroni[etcd3]" psycopg2-binary && \
    apt-get clean && rm -rf /var/lib/apt/lists/*
ENTRYPOINT ["patroni"]
