# Atualização para .NET 10 LTS

Executada em 04/09/2026 por solicitação do usuário.

- SDK oficial Microsoft 10.0.400 instalado pelo winget; instalador com hash validado e saída de instalação bem-sucedida.
- `global.json` seleciona 10.0.400, aceita patches da mesma faixa e não aceita versões preview.
- Projeto alterado de `net9.0` para `net10.0`.
- Pacotes ASP.NET Core Diagnostics/Identity e Entity Framework Core SQL Server/Tools alinhados em 10.0.11.
- SQL Server, layout e regras de negócio preservados; nenhuma migration aplicada ao banco.

## Verificação

`dotnet build recepcaototem.sln --configuration Release --nologo`

Resultado: restauração e compilação concluídas, 0 avisos, 0 erros. Saída em `recepcaototem/bin/Release/net10.0/recepcaototem.dll`.

Não foram executados testes funcionais ou de banco nesta atualização. Compilação não comprova conectividade com SQL Server nem configuração do ambiente de produção.

## Ambiente de execução

Máquinas de desenvolvimento precisam do SDK compatível com `global.json`. O servidor precisa do runtime ASP.NET Core 10 compatível; para IIS, instalar o Hosting Bundle correspondente. A hospedagem de produção não foi alterada neste trabalho.

Fonte das versões: https://dotnet.microsoft.com/en-us/download/dotnet/10.0
