<p align="center">
  <img src="ManusReader/assets/manus-reader-logo.png" alt="ManusReader – Ge varje roll en röst" width="320">
</p>

# ManusReader

ManusReader hjälper dig att öva in dina repliker i ett teatermanus. Datorn läser upp de andra rollerna med talsyntes och väntar medan du säger dina egna.

## Vad programmet gör

- Läser in manus som textfil (`.txt`) eller Word-dokument (`.docx`).
- Hittar rollerna automatiskt, antingen från formatet `ROLL: replik` eller från fetstilta rollnamn i Word.
- Kan tolka röriga Word-manus med en lokal AI-modell via [Ollama](https://ollama.com) (valfritt, laddas ner automatiskt).
- Låter dig ge varje roll en egen röst och karaktär (vuxen, äldre eller barn), med tempo och tonhöjd.
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

## Röster: Windows, Piper eller Azure

Under **Inställningar → TTS-inställningar** (Ctrl+T) väljer du vilken talsyntes programmet ska använda.

| | Windows-röster | Piper | Azure Neural TTS |
|---|---|---|---|
| Kostnad | Gratis | Gratis | Kostar pengar |
| Fungerar offline | Ja | Ja | Nej |
| Ljudkvalitet | Enklare | Naturlig | Mycket naturlig |
| Kräver | Inget | En nedladdning (20 MB + 60–80 MB per röst) | Microsoft Azure-konto |

### Windows-röster (standard)

- Gratis och fungerar offline.
- Programmet använder de röster som är installerade i Windows.
- Lägg till fler röster, till exempel svenska, under **Inställningar → Tid och språk → Tal → Lägg till röster** i Windows.

### Piper (gratis AI-röster, rekommenderas)

[Piper](https://github.com/rhasspy/piper) är en fri talsyntes som körs helt på din egen dator. Den låter betydligt mer naturlig än Windows-rösterna och kostar ingenting.

1. Öppna **TTS-inställningar** och välj **Piper**.
2. Markera en röst och klicka på **Ladda ner**. Första gången hämtas även själva Piper (ca 20 MB). Varje röst är ca 60 MB.
3. Klicka på **Testa** och sedan **OK**.

Det finns fem svenska röster att välja mellan. Några av dem kan du [lyssna på här](https://rhasspy.github.io/piper-samples/) innan du laddar ner.

| Röst | Typ | Skapad av | Licens |
|---|---|---|---|
| Alma | Kvinna | [Daniel Nylander](https://github.com/yeager/piper-voices-sv) | CC BY 4.0 |
| Lisa | Kvinna | [rhasspy/piper-voices](https://huggingface.co/rhasspy/piper-voices) | Ej angiven |
| Axel | Man | [Daniel Nylander](https://github.com/yeager/piper-voices-sv) | CC BY 4.0 |
| Daniel | Man | [Daniel Nylander](https://github.com/yeager/piper-voices-sv) | CC BY 4.0 |
| NST | Man, mörk | KBLab / Kungliga biblioteket | CC0 |

Piper och rösterna laddas ner från sina officiella källor (GitHub och Hugging Face) till `%LOCALAPPDATA%\ManusReader\piper`. De ingår alltså inte i ManusReader.exe. Piper själv har MIT-licens och använder espeak-ng (GPL-3.0) för uttal.

### Karaktärer

Varje roll har en **karaktär** som styr vilka röster som visas och vilken tonhöjd och vilket tempo rollen börjar med:

| Karaktär | Röster | Förinställning |
|---|---|---|
| Vuxen kvinna | Kvinnoröster | Normal |
| Vuxen man | Mansröster | Normal |
| Äldre kvinna | Kvinnoröster | Lite mörkare och långsammare |
| Äldre man | Mansröster | Lite mörkare och långsammare |
| Barn, flicka | Kvinnoröster | Ljusare röst |
| Barn, pojke | Kvinnoröster | Ljusare röst, lite mörkare än flicka |

Det finns inga riktiga svenska barnröster, så barnrösterna görs av en kvinnoröst med höjd tonhöjd. Programmet gissar karaktären från rollnamnet, till exempel MORMOR, POJKEN eller ANNA. Du kan ändra karaktären och finjustera med reglagen. Bocka i **Visa alla röster** om du vill välja en röst som inte passar karaktären.

**🎲 Slumpa röst:** fyll i en ålder (valfritt) och klicka på **Slumpa röst**. Programmet väljer då en passande röst och ett tempo och en tonhöjd som passar könet och åldern, med lite variation varje gång. En sjuåring blir ljus och pigg och en 80-åring mörk och långsam. Klicka igen om du vill ha ett nytt förslag, och justera sedan med reglagen. Lämnar du åldern tom används en typisk ålder för karaktären.

### Azure Neural TTS (valfritt)

Har du tillgång till Azure är det värt att testa. Rösterna låter förvånansvärt naturliga och gör det betydligt roligare att repetera. 🎭

> **Bra att veta innan du börjar:**
> - Du behöver ett eget konto hos **Microsoft Azure**.
> - **Användningen kostar pengar.** Azure tar betalt för mängden text som läses upp. Microsoft har haft en gratisnivå med en begränsad kvot, men villkor och priser kan ändras. Kontrollera aktuella priser på [Azure Speech-prissidan](https://azure.microsoft.com/pricing/details/cognitive-services/speech-services/), och lägg gärna in en budgetvarning i Azure.
> - Du behöver internetuppkoppling.
> - Ingenting skickas till Azure om du inte själv väljer Azure i inställningarna. Standardvalet är Windows-rösterna, och både dem och Piper är gratis.

1. Skapa en **Speech**-resurs i [Azure-portalen](https://portal.azure.com).
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

I Word-dokument räcker det att rollnamnet står i **fetstil**, antingen på en egen rad eller i början av repliken. Programmet förstår även hur riktiga manus brukar se ut:

- **Scenanvisningar** i *kursiv stil* eller (inom parentes) visas men läses inte upp, inte heller när de står mitt i en replik.
- **Sångtexter i VERSALER** räknas till den roll som sjunger, inte som nya roller.
- **Rubriker** som "SCEN 2:3" eller "# 21 - CABINET TOMORROW" visas men läses inte upp.
- **Titelsida och rollista** före första "AKT" eller "SCEN" tolkas inte som repliker. Står det en ålder i rollistan, till exempel "ANNIE 11 år", får rollen den åldern och en passande röst automatiskt.
- **Radbrytningar inuti stycken** (Skift+Enter) hanteras, så rollnamn, anvisning och replik kan stå i samma stycke.

Har manuset fetstilta rollnamn är **Öppna Word-manus** oftast både snabbare och säkrare än AI-tolkningen. Programmet föreslår det automatiskt.

## AI-tolkning med Ollama (valfritt)

Följer manuset inget tydligt format kan du använda **Arkiv → Öppna Word-manus med AI (Ollama)**. AI-modellen körs helt lokalt på din dator, och inget skickas till internet.

Du behöver inte installera något själv. Första gången frågar ManusReader om den får ladda ner AI-delen, cirka 3,2 GB. Det är en engångsnedladdning.

- **[Ollama](https://github.com/ollama/ollama) 0.32.6** (MIT-licens), AI-motorn
- **AI-modellen `llama3.2:3b`**

ManusReader använder alltid sin egen, låsta version av Ollama och modellen, så resultatet blir detsamma oavsett dator. Filerna sparas i `%LOCALAPPDATA%\ManusReader\ollama`. AI-motorn körs på en egen port (11435) och krockar inte med en Ollama som du kanske redan har installerat. Den startas när den behövs och stängs när du avslutar ManusReader.

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
