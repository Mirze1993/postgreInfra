#!/bin/bash
# HA cluster monitorinqi - heç bir xarici alət lazım deyil

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'

check() {
    echo "═══════════════════════════════════════════════════════"
    echo "  HA Cluster Status - $(date '+%Y-%m-%d %H:%M:%S')"
    echo "═══════════════════════════════════════════════════════"

    # 1) Patroni cluster vəziyyəti
    echo ""
    echo "▶ Patroni cluster:"
    if docker exec pg1 patronictl -c /etc/patroni.yml list 2>/dev/null; then
        echo -e "${GREEN}✓ Cluster cavab verir${NC}"
    else
        echo -e "${RED}✗ Patroni cavab vermir!${NC}"
    fi

    # 2) Replikasiya gecikməsi (hansı node primary-dırsa, oradan soruş)
    echo ""
    echo "▶ Replikasiya:"
    REPL=$(docker exec pg1 psql -U postgres -d postgres -t -A -c \
        "SELECT pg_is_in_recovery();" 2>/dev/null | tr -d ' \n')

    if [ "$REPL" = "f" ]; then
        PRIMARY="pg1"
    else
        PRIMARY="pg2"
    fi

    LAG=$(docker exec $PRIMARY psql -U postgres -d postgres -t -A -c \
        "SELECT COALESCE(EXTRACT(EPOCH FROM replay_lag)::int::text, 'null') FROM pg_stat_replication LIMIT 1;" \
        2>/dev/null | tr -d ' \n')

    if [ -z "$LAG" ] || [ "$LAG" = "null" ]; then
        echo -e "${RED}✗ Replika qoşulmayıb!${NC}"
    elif [ "$LAG" -gt 30 ]; then
        echo -e "${RED}✗ Replikasiya gecikməsi: ${LAG}s${NC}"
    elif [ "$LAG" -gt 5 ]; then
        echo -e "${YELLOW}⚠ Replikasiya gecikməsi: ${LAG}s${NC}"
    else
        echo -e "${GREEN}✓ Replika sinxrondur (${LAG}s)${NC}"
    fi

    # 3) WAL arxivi
    echo ""
    echo "▶ WAL arxivi (PITR):"
    ARCH=$(docker exec $PRIMARY psql -U postgres -d postgres -t -A -c \
        "SELECT failed_count || '|' || COALESCE(EXTRACT(EPOCH FROM (now() - last_archived_time))::int::text, '999') FROM pg_stat_archiver;" \
        2>/dev/null | tr -d ' \n')

    FAILED=$(echo $ARCH | cut -d'|' -f1)
    AGE=$(echo $ARCH | cut -d'|' -f2)

    if [ "$FAILED" = "0" ] && [ "$AGE" -lt 120 ]; then
        echo -e "${GREEN}✓ WAL arxivi işləyir (son: ${AGE}s əvvəl)${NC}"
    elif [ "$FAILED" != "0" ]; then
        echo -e "${RED}✗ WAL arxivi uğursuzluqları: $FAILED${NC}"
    else
        echo -e "${YELLOW}⚠ WAL arxivi ləngdir (son: ${AGE}s əvvəl)${NC}"
    fi

    # 4) Bağlantı sayı
    echo ""
    echo "▶ Bağlantılar:"
    CONNS=$(docker exec $PRIMARY psql -U postgres -d postgres -t -A -c \
        "SELECT count(*) || '/' || current_setting('max_connections') FROM pg_stat_activity WHERE backend_type='client backend';" \
        2>/dev/null | tr -d ' \n')
    echo "  Aktiv: $CONNS"

    # 5) Docker konteyner statusu
    echo ""
    echo "▶ Konteynerlər:"
    docker ps --filter "name=pg1" --filter "name=pg2" --filter "name=etcd" \
        --format "  {{.Names}}: {{.Status}}" 2>/dev/null

    echo ""
    echo "═══════════════════════════════════════════════════════"
    echo ""
}

check

# Əgər arqument "watch" verilibsə, hər 10 saniyədə təkrar et
if [ "$1" = "watch" ]; then
    while true; do
        sleep 10
        clear
        check
    done
fi
