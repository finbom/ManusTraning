<p align="center">
  <img src="ManusReader/assets/manus-reader-logo.png" alt="ManusReader – Ge varje roll en röst" width="320">
</p>

# ManusReader

ManusReader hjälper dig att öva in dina repliker i ett teatermanus. Datorn läser upp de andra rollerna med talsyntes och väntar medan du säger dina egna.

## Vad programmet gör

- Läser in manus som textfil (`.txt`) eller Word-dokument (`.docx`).
- Hittar rollerna automatiskt, antingen från formatet `ROLL: replik` eller från fetstilta rollnamn i Word.
- Kan tolka röriga Word-manus med en lokal AI-modell via [Ollama](https://ollama.com) (valfritt).
- Låter dig ge varje roll en egen röst, med kön, tempo och tonhöjd.
- Låter dig välja vilka repliker datorn ska läsa. Dina egna avmarkerar du.
- Har två övningslägen:
  - **Starta (manuellt):** du stegar själv mellan replikerna med Mellanslag och piltangenterna.
  - **Repetera (automatiskt):** datorn läser sina repliker och gör en paus där du ska säga din.
- Exporterar en repetition som MP3, så att du kan öva med hörlurar utan datorn.
- Sparar ditt arbete som en projektfil (`.mrp`) med roller, röster och markeringar.

## Ladda ner färdigt program för Windows

1. Gå till [Releases](https://github.com/finbom/ManusTraning/releases/latest).
2. Ladda ner `ManusReader.exe`.
3. Starta filen. Du behöver inte installera något, och .NET följer med i filen.

Programmet kräver 64-bitars Windows 10 eller 11.

> Windows SmartScreen kan varna första gången du startar programmet, eftersom filen inte är signerad. Klicka på **Mer information** och sedan **Kör ändå**.

## Kompilera själv

Du behöver [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) på Windows.

```bat
git clone https://github.com/finbom/ManusTraning.git
cd ManusTraning\ManusReader
build.bat
```

`build.bat` skapar en fristående fil i `ManusReader\publish\ManusReader.exe`.

Under utveckling kan du i stället köra:

```bat
dotnet run --project ManusReader\ManusReader.csproj
```

## Röster: Windows eller Azure

Under **Inställningar → TTS-inställningar** (Ctrl+T) väljer du vilken talsyntes programmet ska använda.

### Windows-röster (standard)

- Gratis och fungerar offline.
- Programmet använder de röster som är installerade i Windows.
- Lägg till fler röster, till exempel svenska, under **Inställningar → Tid och språk → Tal → Lägg till röster** i Windows.

### Azure Neural TTS (valfritt)

Azure-rösterna låter mer naturliga, men kräver ett Azure-konto och internetuppkoppling.

1. Skapa en **Speech**-resurs i [Azure-portalen](https://portal.azure.com). Gratisnivån (F0) räcker gott för att öva.
2. Kopiera en av resursens nycklar och dess region, till exempel `swedencentral`.
3. Välj **Azure** i TTS-inställningarna och fyll i nyckel, region och språk (`sv-SE`).
4. Kontrollera uppgifterna med **Testa Azure-anslutning**.

Bland Azure-rösterna finns bland annat svenska Mattias, Sofie och Hillevi samt röster på engelska, tyska och franska.

Inställningarna sparas i `settings.json` bredvid programfilen. Den filen innehåller din Azure-nyckel, så dela den inte med någon.

## Manusformat

En textfil skriver du med rollnamnet följt av kolon. Scenanvisningar skriver du inom parentes:

```text
ANNA: Välkommen till teatern, allesammans!
BJÖRN: Tack så mycket. Det är en stor ära att vara här.
(Publiken applåderar)
ANNA: Hur mår du egentligen, Björn?
```

Ett komplett exempel finns i [exempelmanus.txt](ManusReader/exempelmanus.txt).

I Word-dokument räcker det att rollnamnet står i **fetstil** i början av stycket.

## AI-tolkning med Ollama (valfritt)

Följer manuset inget tydligt format kan du använda **Arkiv → Öppna Word-manus med AI (Ollama)**. AI-modellen körs helt lokalt på din dator.

1. Installera [Ollama](https://ollama.com/download).
2. Kör [setup-ollama.bat](ManusReader/setup-ollama.bat). Skriptet laddar ner modellen `llama3.2`, som är cirka 2 GB.
3. Kontrollera att Ollama körs (`ollama serve`).

Rader som AI:n inte kan koppla till en roll markeras i orange. Högerklicka på en sådan rad för att ge den en roll.

## Kortkommandon

| Tangent | Funktion |
|---|---|
| Mellanslag / → / ↓ | Nästa replik. I repetitionsläget hoppar den över pausen. |
| ← / ↑ | Föregående replik (manuellt läge) |
| Ctrl+O | Öppna textmanus |
| Ctrl+W | Öppna Word-manus |
| Ctrl+S | Spara projekt |
| Ctrl+T | TTS-inställningar |
