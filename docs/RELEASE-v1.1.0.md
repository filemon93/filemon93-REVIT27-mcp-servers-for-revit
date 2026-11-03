# Revit MCP 27 — wymiarowanie rzutów i instalator dla pracowników

**Pobierz `RevitMCP27-1.1.0-Windows-x64.zip` z sekcji Assets poniżej.** To kompletna paczka dla Windows x64 i Revit 2027. Archiwa „Source code” są przeznaczone dla programistów.

## Instalacja w kilku krokach

1. Rozpakuj ZIP, zamknij Revit 2027 i uruchom `Install-2027.bat`.
2. Przenieś wpis z `%LOCALAPPDATA%\RevitMCP27\agent-config.json` do konfiguracji MCP agenta. Dla Codex użyj wygenerowanego `agent-config.toml`.
3. Uruchom Revit, otwórz model i wybierz **Add-Ins → Revit MCP Switch**. Zamknij komunikat.
4. Zrestartuj połączenie MCP w agencie i poproś o `get_current_view_info` oraz `get_dimension_types`.

**Nie trzeba instalować Node.js, npm ani SDK.** Wszystko jest w paczce. Instalator nie wymaga administratora, sprawdza SHA256, zachowuje kopie manifestów i nie zamyka ani nie zapisuje modeli. Agent i Revit muszą działać na tym samym komputerze; most nasłuchuje tylko na `127.0.0.1:8080`.

[Pełna instrukcja po polsku](https://github.com/filemon93/filemon93-REVIT27-mcp-servers-for-revit/blob/v1.1.0/docs/START-PL.md) · [Parametry wymiarowania](https://github.com/filemon93/filemon93-REVIT27-mcp-servers-for-revit/blob/v1.1.0/docs/DIMENSIONING.md) · [Weryfikacja](https://github.com/filemon93/filemon93-REVIT27-mcp-servers-for-revit/blob/v1.1.0/docs/VALIDATION.md)

## Co dodano

- `get_dimension_types`: lista liniowych stylów wymiarowania.
- `dimension_walls_by_guides`: ciągi przez lica lub rdzeń ścian, prowadzone prostymi liniami lub płaszczyznami odniesienia, z minimalnym segmentem i raportem pominięć.
- Poprawione `create_dimensions`: jawne referencje lub elementy, działające `linePoint`, obsługa 64-bitowych ID, kontrola poprawności i częściowych błędów, jedno Cofnij dla serii.
- Gotowy instalator i konfiguracje agentów. Serwer z tego forka jest dołączony do ZIP; nie korzystaj z pakietu npm projektu źródłowego.
- Zaktualizowane zależności serwera; audyt npm bez zgłoszonych podatności.

## Zakres

Wymiarowanie prowadnicami obsługuje proste, pionowe ściany podstawowe w aktywnym dokumencie i rzuty kondygnacji/sufitów/konstrukcji. Tryb rdzenia jest **eksperymentalny** — korzysta z wewnętrznych referencji Revita, których położenie jest sprawdzane próbnymi wymiarami. Brak obsługi linków, ścian łukowych/kurtynowych/pochyłych/złożonych i automatycznego wymiarowania ościeży. Ponowne wywołanie tworzy kolejną serię.

Zmiana kompatybilności: `create_dimensions` wymaga referencji lub identyfikatorów elementów; samo podanie punktów nie uruchamia już zgadywania najbliższych referencji.

Weryfikacja: kompilacja C#/TypeScript, test klienta MCP przez STDIO oraz **14 kontroli wymiarowania w Revit 2027.3**, w osobnym niezapisanym dokumencie testowym. Wydanie dotyczy Revit 2027; nie certyfikuje innych roczników ani wszystkich możliwych rodzin i konfiguracji modelu.
