# Revit MCP 27 — instrukcja dla pracowników

Pobierz **RevitMCP27-1.1.0-Windows-x64.zip** z [Releases tego forka](https://github.com/filemon93/filemon93-REVIT27-mcp-servers-for-revit/releases/latest). To kompletna paczka Windows x64 dla **Revit 2027**: dodatek, serwer MCP, Node.js 22 i zależności. Nie wymaga npm, SDK, uprawnień administratora ani konta GitHub. Automatyczne archiwa „Source code” zawierają źródła, a nie gotowy instalator.

## Instalacja

1. Rozpakuj ZIP do lokalnego folderu.
2. Zamknij Revit 2027. Instalator nie zamyka procesów ani nie zapisuje projektów.
3. Uruchom `Install-2027.bat`. Instalacja jest przypisana do bieżącego użytkownika Windows. Pliki trafiają do `%LOCALAPPDATA%\RevitMCP27`, manifest do `%APPDATA%\Autodesk\Revit\Addins\2027`.
4. Instalator sprawdza SHA256 plików i zapisuje kopie zastępowanych manifestów w `backups`. Wspólne manifesty zachowują inne dodatki. Wykryta instalacja systemowa wymaga usunięcia jej wpisu przez administratora.
5. Otwórz Revit, zaakceptuj wczytanie dodatku, jeśli ufasz źródłu, otwórz projekt i wybierz **Add-Ins → Revit MCP Switch**. Komunikat „Open Server” oznacza uruchomienie mostu; zamknij komunikat.
6. Podłącz serwer w agencie, korzystając z plików wygenerowanych przez instalator. Serwer STDIO uruchamia agent; nie trzeba otwierać osobnej konsoli.

Kontrola paczki bez instalacji: `powershell -NoProfile -File .\Install.ps1 -CheckOnly`.

## Podłączenie agenta

Agent i Revit muszą działać na tym samym komputerze Windows. Most używa TCP `127.0.0.1:8080`; nie wymaga otwierania portu w zaporze. Uruchom tylko jedną instancję tego mostu. Serwer MCP komunikuje się z agentem przez STDIO, nie przez HTTP/SSE.

**Klienci obsługujący JSON `mcpServers`**, np. Claude Desktop: skopiuj wpis `revit-mcp-27` z `%LOCALAPPDATA%\RevitMCP27\agent-config.json` do konfiguracji MCP swojego agenta. Połącz go z istniejącymi wpisami, nie zastępuj całej konfiguracji. Plik zawiera już poprawne ścieżki dla Twojego konta:

```json
{
  "mcpServers": {
    "revit-mcp-27": {
      "command": "C:/Users/TWOJ_LOGIN/AppData/Local/RevitMCP27/INSTALACJA/server/node.exe",
      "args": ["C:/Users/TWOJ_LOGIN/AppData/Local/RevitMCP27/INSTALACJA/server/build/index.js"]
    }
  }
}
```

**Codex:** dopisz blok z `%LOCALAPPDATA%\RevitMCP27\agent-config.toml` do `%USERPROFILE%\.codex\config.toml`. Sekcja ma postać `[mcp_servers.revit-mcp-27]` z polami `command` i `args`. Nie dodawaj drugiej sekcji o tej samej nazwie. Konfiguracja STDIO: [oficjalna dokumentacja OpenAI](https://developers.openai.com/codex/mcp).

W kliencie z formularzem konfiguracji wybierz transport STDIO/local process, a `command` i `args` przepisz z wygenerowanego JSON. Klienci używający innego schematu, np. `servers`, wymagają przeniesienia tych samych wartości do ich własnego formatu. Zrestartuj połączenie MCP/agenta po zmianie konfiguracji.

Nie używaj `npx mcp-server-for-revit` do instalacji tego wydania: ta nazwa wskazuje pakiet projektu źródłowego, a nie tego forka.

## Pierwszy test

Poproś agenta: „Wywołaj get_current_view_info, a potem get_dimension_types. Podaj nazwę widoku i dostępne style wymiarowania”. Następnie narysuj na rzucie prostą Detail Line przecinającą ściany, zaznacz ją i poproś: „Odczytaj zaznaczoną prowadnicę i utwórz wymiarowanie lic ścian wzdłuż niej przez dimension_walls_by_guides. Minimalny segment 10 mm. Podaj utworzone ID i wszystkie pominięcia”.

Wymiarowanie oraz inne narzędzia mogą zmieniać model. Zmiany są wykonywane w projekcie otwartym w Revicie; projekt nie jest automatycznie zapisywany ani synchronizowany. Wynik narzędzia może być częściowym sukcesem, dlatego agent powinien zawsze czytać `results`, `failed`/`skipped` i `warnings`.

## Wymiarowanie i problemy z połączeniem

- Opis parametrów i ograniczeń: [DIMENSIONING.md](DIMENSIONING.md).
- Brak połączenia: sprawdź otwarty model, przycisk MCP Switch i port 8080. Zamknij modalne okna Revita i zakończ tryb edycji grupy/szkicu przed wywołaniem narzędzia.
- Brak nowych narzędzi: serwer i dodatek muszą pochodzić z tego samego wydania. Po instalacji uruchom ponownie Revit oraz połączenie MCP.
- Błąd zajętego portu: zamknij most w drugiej instancji Revita. Nie uruchamiaj jednocześnie starszego dodatku o tym samym identyfikatorze.
- Aktualizacja: rozpakuj nowe wydanie, zamknij Revit i uruchom instalator. Ponownie przenieś wygenerowaną konfigurację do agenta, ponieważ ścieżka instalacji się zmienia. Poprzednie katalogi i kopie manifestów pozostają do ręcznego przywrócenia.
- Usunięcie: przy zamkniętym Revicie usuń wpis tego dodatku z manifestu `mcp-servers-for-revit.addin` i wpis `revit-mcp-27` z konfiguracji agenta. Zachowane kopie w `backups` umożliwiają przywrócenie poprzedniej konfiguracji.

Polityka dostępu do modelu i danych wysyłanych do dostawcy agenta zależy od konfiguracji tego agenta oraz zasad firmy.
