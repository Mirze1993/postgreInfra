FROM postgres:18

# pgBackRest, orafce və contrib genişlənmələrini quraşdır
RUN apt-get update \
&& apt-get install -y --no-install-recommends \
pgbackrest \
postgresql-18-orafce \
postgresql-contrib \
&& rm -rf /var/lib/apt/lists/* \
&& mkdir -p /etc/pgbackrest /var/lib/pgbackrest /var/spool/pgbackrest /var/log/pgbackrest \
&& chown -R postgres:postgres /var/lib/pgbackrest /var/spool/pgbackrest /var/log/pgbackrest
