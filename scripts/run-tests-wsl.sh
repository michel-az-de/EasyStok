#!/bin/bash
# Roda testes via WSL, EXCLUINDO Mobile (sem workload maui-android) e IntegrationTests (sem Docker aqui).
set +e
cd /mnt/c/easy/EasyStok/.claude/worktrees/wt-deploy-fly
echo "PWD: $(pwd)"
echo "Iniciando dotnet test em $(date)..."

# Projetos de teste para deploy (excluindo Mobile e IntegrationTests):
projetos=(
    "EasyStock.ArchitectureTests/EasyStock.ArchitectureTests.csproj"
    "EasyStock.Domain.Tests/EasyStock.Domain.Tests.csproj"
    "EasyStock.Application.Tests/EasyStock.Application.Tests.csproj"
    "EasyStock.Api.UnitTests/EasyStock.Api.UnitTests.csproj"
    "EasyStock.Web.UnitTests/EasyStock.Web.UnitTests.csproj"
)

> /tmp/easystok-test.log
total_pass=0
total_fail=0
exit_code=0

for p in "${projetos[@]}"; do
    echo "" | tee -a /tmp/easystok-test.log
    echo "=== Testing $p em $(date) ===" | tee -a /tmp/easystok-test.log
    dotnet test "$p" --logger 'console;verbosity=minimal' --nologo >> /tmp/easystok-test.log 2>&1
    pec=$?
    if [ $pec -ne 0 ]; then
        echo "FAILED: $p (exit $pec)" | tee -a /tmp/easystok-test.log
        exit_code=$pec
    fi
done

echo "" | tee -a /tmp/easystok-test.log
echo "EXIT_CODE=$exit_code em $(date)" | tee -a /tmp/easystok-test.log
echo "--- summary --- "
grep -E 'Aprovado!|Falha|Passed!|Failed!' /tmp/easystok-test.log
echo "--- last 30 lines ---"
tail -30 /tmp/easystok-test.log
