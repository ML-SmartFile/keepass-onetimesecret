# OneTimeSecret für KeePass — Benutzerhandbuch

*[This guide in English](USER_GUIDE.md)*

Diese Anleitung richtet sich an alle, die KeePass zur Passwortverwaltung nutzen, aber
nichts mit technischem Fachjargon anfangen können. Sie führt Schritt für Schritt durch
die Installation, die Einrichtung und die Nutzung des Plugins, um ein Passwort sicher an
jemand anderen weiterzugeben — ohne Vorwissen vorauszusetzen.

Unbekannte Begriffe werden beim ersten Auftreten erklärt.

> Hinweis: Die Bildschirmfotos in dieser Anleitung zeigen die Plugin-Oberfläche auf
> Englisch — das Plugin selbst ist (noch) nicht auf Deutsch übersetzt. Die
> Menü-/Fensterbezeichnungen im Text sind daher auf Englisch angegeben, damit Sie sie am
> Bildschirm wiederfinden; die Erklärungen drumherum sind auf Deutsch.

> Suchen Sie die technische Referenz? Siehe `README.md` (Englisch).

---

## Was dieses Plugin eigentlich macht

Normalerweise, wenn Sie jemandem ein Passwort schicken müssen, tippen Sie es vielleicht
in eine Nachricht, schreiben es per E-Mail oder sagen es am Telefon — in allen Fällen
bleibt eine dauerhafte Kopie im Chatverlauf oder Postfach liegen, für jeden zugänglich,
der später Zugriff darauf bekommt.

Dieses Plugin erstellt stattdessen einen **Einmal-Link**: eine Webadresse, die das
Passwort genau einmal anzeigt. Sobald die andere Person den Link öffnet und das Passwort
gesehen hat, funktioniert der Link nicht mehr — auch nicht mehr für Sie. Öffnet ihn
niemand, läuft er automatisch nach einer von Ihnen gewählten Zeitspanne ab (z. B. nach
einem Tag). So oder so bleibt nichts liegen, das später gefunden werden könnte.

Das funktioniert, indem das Passwort an einen Dienst namens **OneTimeSecret**
(onetimesecret.com) gesendet wird, der im Gegenzug diesen Einmal-Link zurückgibt. Diesen
schicken Sie dann wie gewohnt an die andere Person (Chat, E-Mail, egal) — es ist nur ein
Link, nicht das Passwort selbst, daher unbedenklich über normale Kanäle zu verschicken.

---

## Teil 1 — Plugin installieren

**Was Sie vorher brauchen:** KeePass 2.x bereits installiert, sowie eine funktionierende
Internetverbindung zum Herunterladen des Plugins.

1. Gehen Sie zur
   [Releases-Seite](https://github.com/ML-SmartFile/keepass-onetimesecret/releases/latest)
   und laden Sie die Datei `OneTimeSecret.dll` herunter.

2. Öffnen Sie KeePass.

3. Klicken Sie oben in der Menüleiste auf **Tools**, dann auf **Plugins**.

   ![KeePass Tools-Menü mit markiertem Plugins-Eintrag](docs/screenshots/tools-plugins-menu.png)

4. Klicken Sie im sich öffnenden Plugins-Fenster auf die Schaltfläche **Open Folder**
   (öffnet den Ordner, in dem KeePass seine Plugins ablegt). Es öffnet sich ein normales
   Windows-Ordnerfenster.

   ![Der Plugins-Dialog mit markierter Open-Folder-Schaltfläche](docs/screenshots/plugins-dialog-folder-icon.png)

5. Kopieren Sie die in Schritt 1 heruntergeladene Datei `OneTimeSecret.dll` in diesen
   Ordner (per Ziehen oder Kopieren-und-Einfügen wie jede andere Datei).

6. Schließen Sie KeePass vollständig und öffnen Sie es erneut. (Plugins werden nur beim
   Start von KeePass geladen — das Plugins-Fenster zu schließen genügt nicht.)

7. Zur Kontrolle: Öffnen Sie erneut **Tools → Plugins**. Dort sollte jetzt
   "OneTimeSecret" mit einer Versionsnummer aufgeführt sein.

   ![Der Plugins-Dialog mit aufgelistetem OneTimeSecret](docs/screenshots/plugins-dialog-listed.png)

Falls es nicht aufgelistet ist: prüfen Sie, ob die Datei in Schritt 5 wirklich im
richtigen Ordner liegt, und ob KeePass vollständig neu gestartet wurde (nicht nur
minimiert).

---

## Teil 2 — Einrichten (optional, aber empfehlenswert)

Sie können diesen ganzen Teil überspringen und das Plugin trotzdem nutzen — es
funktioniert dann **anonym** und benötigt nichts von Ihnen. Dieser Teil betrifft nur
zwei optionale Erweiterungen: die Nutzung eines eigenen OneTimeSecret-Kontos (falls
vorhanden) und das Anpassen der Voreinstellungen (z. B. wie lange ein geteilter Link
gültig sein soll).

1. Klicken Sie in KeePass auf **Tools**, dann auf **OneTimeSecret Options…**

   ![Tools-Menü mit markiertem OneTimeSecret Options](docs/screenshots/tools-options-menu.png)

2. Es öffnet sich ein Einstellungsfenster mit mehreren Bereichen:

   - **Server** — hier nichts ändern, außer jemand (z. B. Ihre IT-Abteilung) hat Ihnen
     eine bestimmte Server-Adresse genannt. Die Voreinstellung passt für die meisten.
   - **Credentials (Zugangsdaten)** — hier verbinden Sie optional Ihr eigenes
     OneTimeSecret-Konto, falls vorhanden:
     - Bleibt das Feld leer, werden Shares anonym verschickt — das funktioniert für den
       Alltag problemlos.
     - Haben Sie ein OneTimeSecret-Konto und möchten es nutzen: Legen Sie in KeePass
       einen Eintrag an (oder wählen Sie einen bestehenden), der als "Benutzername" Ihre
       OneTimeSecret-Konto-E-Mail und als "Passwort" Ihren OneTimeSecret-API-Token
       enthält (ein spezieller Code aus den Kontoeinstellungen von OneTimeSecret, nicht
       Ihr eigentliches Login-Passwort). Wählen Sie diesen Eintrag dann hier aus. E-Mail
       und Token bleiben dabei durchgehend in Ihrer KeePass-Datenbank — das Plugin liest
       sie nur im Moment des Teilens aus.
   - **Share defaults (Standardeinstellungen)** — gelten, sofern Sie sie nicht pro
     Freigabe ändern:
     - **Default lifetime (Standard-Gültigkeitsdauer)** — wie lange ein geteilter Link
       gültig bleibt, falls ihn niemand öffnet (z. B. "1 day" = 1 Tag).
     - **Was enthalten ist** — welche Angaben eines Passwort-Eintrags (Titel,
       Benutzername, Passwort, Internetadresse, Notizen) standardmäßig mitgeschickt
       werden. Passwort ist standardmäßig aktiv; den Rest können Sie an- oder abwählen.
     - **Passphrase** — siehe Erklärung in Teil 3. Das Standardverhalten lässt sich hier
       festlegen.

   ![Das OneTimeSecret-Options-Fenster](docs/screenshots/options-window.png)

3. Klicken Sie auf **OK**, um die Einstellungen zu speichern und das Fenster zu
   schließen.

---

## Teil 3 — Ein Passwort teilen

1. Suchen Sie in KeePass den Eintrag (das gespeicherte Passwort), den Sie teilen
   möchten, und **klicken Sie mit der rechten Maustaste** darauf.

2. Fahren Sie im Rechtsklick-Menü mit der Maus über **OneTimeSecret**. Ein kleines
   Untermenü erscheint — die zwei relevanten Einträge sind **Share…** und
   **Quick Share…**

   ![Rechtsklick-Menü auf einem Eintrag mit dem OneTimeSecret-Untermenü](docs/screenshots/context-submenu.png)

### Variante A: "Share…" — wenn Sie vorher alles noch einmal prüfen möchten

1. Klicken Sie auf **Share…**. Ein Fenster öffnet sich, das genau zeigt, was
   verschickt werden soll — standardmäßig Benutzername und Passwort dieses Eintrags.

2. **Kontrollkästchen** oben lassen Sie auswählen, was enthalten sein soll (z. B.
   zusätzlich die Internetadresse, oder ohne Benutzername). Ein Vorschaufeld zeigt den
   exakten Text, der verschickt wird — standardmäßig unkenntlich gemacht; Klick auf
   "show", um ihn vorher zu lesen.

3. **Gültigkeitsdauer** — über ein Dropdown wählen Sie, wie lange der Link gültig
   bleiben soll (z. B. 1 Stunde, 1 Tag, 7 Tage), bevor er automatisch abläuft, auch wenn
   er nie geöffnet wurde.

4. **Passphrase** — ein *zusätzliches* Passwort, das die andere Person eingeben muss,
   bevor sie das geteilte Passwort sehen kann. Eine Art zweites Schloss zusätzlich zum
   Link. Ihre Optionen:
   - **None (keine)** — wer den Link hat, kann ihn ohne weiteres Passwort öffnen.
   - **Generate one (eine generieren)** — das Plugin erzeugt eine zufällige Passphrase,
     die nach dem Senden angezeigt wird. Diese teilen Sie der anderen Person getrennt
     vom Link mit (z. B. Link per E-Mail, Passphrase am Telefon) — so kann jemand, der
     nur eines von beidem abfängt, trotzdem nicht darauf zugreifen.
   - **Enter one each time (jedes Mal selbst eingeben)** — Sie geben für diese
     Freigabe Ihre eigene Passphrase ein.

5. (Optional) **Recipient email (Empfänger-E-Mail)** — wird dies ausgefüllt, schickt
   OneTimeSecret den Link zusätzlich direkt per E-Mail an diese Adresse.

6. Eine Zeile am unteren Rand zeigt genau, an welchen Server die Informationen
   gesendet werden, damit Sie immer wissen, wohin.

7. Klicken Sie auf **Share**. Ein kurzer Fortschrittsbalken erscheint während des
   Sendens — in der Regel nur wenige Sekunden. (Ein **Cancel**-Button ist während des
   Sendens verfügbar und wird zu **Close**, sobald die Information tatsächlich
   verschickt wurde — ab diesem Punkt kann Abbrechen das "Senden" nicht mehr
   rückgängig machen, da es möglicherweise bereits gespeichert ist.)

   ![Das ausgefüllte Share-Fenster, bereit zum Senden](docs/screenshots/share-window-filled.png)

8. Danach zeigt ein Ergebnisfenster:
   - Den **Link** zum Weitergeben, mit eigener **Copy**-Schaltfläche.
   - Falls eine Passphrase generiert oder eingegeben wurde, wird auch diese mit
     **eigener Copy-Schaltfläche** angezeigt — bewusst getrennt, damit das Kopieren des
     einen nicht das andere in der Zwischenablage überschreibt.
   - Einen **Receipt-Link (Quittungslink)**, ebenfalls mit eigener Copy-Schaltfläche —
     dieser ist nicht für die andere Person bestimmt, sondern ermöglicht Ihnen, die
     Freigabe später vorzeitig zurückzuziehen (siehe Teil 4).

   ![Ergebnisfenster mit Link, Passphrase und Receipt-Link](docs/screenshots/result-window.png)

   > Das Fenster weist außerdem darauf hin, dass sich die Zwischenablage nach wenigen
   > Sekunden automatisch leert — kopieren Sie alles Benötigte, bevor Sie das Fenster
   > schließen.

9. Schicken Sie Link (und Passphrase, falls vorhanden) wie gewohnt an die andere
   Person — Chat, E-Mail, SMS. Wichtig: Wurde eine Passphrase verwendet, schicken Sie
   sie über einen *anderen* Kanal als den Link, damit eine einzelne abgefangene
   Nachricht nicht beides preisgibt.

> **Zur Zwischenablage:** Nach Klick auf "Copy" leert KeePass automatisch nach einem
> kurzen Countdown, was kopiert wurde (ein normales KeePass-Verhalten, nicht spezifisch
> für dieses Plugin). Fügen Sie den Inhalt bei Bedarf sofort ein, statt zu warten.

### Variante B: "Quick Share…" — wenn es einfach schnell gehen soll

1. Klicken Sie statt Share auf **Quick Share…**

2. Ein einzelnes Bestätigungsfenster erscheint mit genauer Angabe, was verschickt
   wird (z. B. "Share user name and password from 'Example Login' with
   eu.onetimesecret.com?"). Klicken Sie **OK** zum Bestätigen oder **Cancel**
   (je nach Systemsprache ggf. "Abbrechen") zum Abbrechen.

   ![Das Quick-Share-Bestätigungsfenster](docs/screenshots/quickshare-confirm.png)

3. Es wird mit den in Teil 2 eingerichteten Standardeinstellungen gesendet (oder den
   eingebauten Standardwerten, falls Teil 2 übersprungen wurde) — kein weiteres Fenster
   zum Ausfüllen.

4. Danach erscheint dasselbe Ergebnisfenster wie bei Variante A, mit Link (und
   Passphrase, falls generiert).

Quick Share ist nur eine schnellere Variante von Share — es fragt immer noch einmal
zur Bestätigung nach, bevor etwas gesendet wird, und zeigt danach immer das
Ergebnisfenster, damit Link und Passphrase nicht verloren gehen.

---

## Teil 4 — Eine Freigabe vorzeitig zurückziehen ("Burn")

Haben Sie einen Link verschickt, es sich aber anders überlegt — er soll *bevor* ihn
jemand öffnet oder bevor er regulär abläuft nicht mehr funktionieren — können Sie ihn
widerrufen, sofern die Quittungs-Option aktiviert war (siehe unten).

**Damit das funktioniert, muss "Write the receipt back to the entry" aktiviert
gewesen sein** (Teil 2, Share defaults), als Sie geteilt haben. War das der Fall, wird
ein kleiner Verweis auf demselben Passwort-Eintrag gespeichert — genau das ermöglicht
das spätere Zurückziehen.

1. Klicken Sie mit der rechten Maustaste auf den zuvor geteilten Eintrag.

2. Im Untermenü **OneTimeSecret** erscheint jetzt zusätzlich **Burn Last Share…**
   unterhalb von Share und Quick Share (nur sichtbar, wenn für diesen Eintrag eine
   aktive Freigabe zum Zurückziehen besteht).

   ![Rechtsklick-Menü mit Burn Last Share](docs/screenshots/burn-menu.png)

3. Klicken Sie darauf, bestätigen Sie im erscheinenden Fenster, und der Link wird
   deaktiviert — versucht jemand danach, ihn zu öffnen, sieht er, dass er nicht mehr
   verfügbar ist.

---

## Gut zu wissen

- **Es wird nichts an einen anderen Ort gesendet als den einen konfigurierten
  Server.** Das Plugin "telefoniert nicht nach Hause", verfolgt keine Nutzung und
  spricht keine andere Adresse an.
- **Wurde in Teil 2 kein Konto verbunden**, werden alle Freigaben anonym verschickt —
  das ist völlig normal und funktioniert genauso.
- **Eine reine `http://`-Adresse wird grundsätzlich abgelehnt**, aus
  Sicherheitsgründen — außer Sie testen ausdrücklich gegen etwas, das auf Ihrem
  eigenen Rechner läuft. Im normalen Gebrauch tritt das nicht auf.
- Geht beim Teilen etwas schief (keine Internetverbindung, falsche Kontodaten usw.),
  zeigt das Plugin eine verständliche Beschreibung des Problems statt eines kryptischen
  Fehlercodes.

---

## Immer noch nicht weiter?

Falls ein Schritt oben nicht zu dem passt, was Sie sehen, oder etwas nicht
funktioniert, schauen Sie in die Abschnitte *Security* und *Configuring it* der
`README.md` (Englisch, technischer) oder wenden Sie sich an die Person, die Ihnen
dieses Plugin eingerichtet hat.
