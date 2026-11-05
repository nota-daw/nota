# Relecture de fr.json par écran

Généré à partir de `Languages/fr.json` et du code : le texte anglais (clé), la traduction, et le fichier où il est utilisé. Les textes sont classés par fichier. Les `{0}` sont des valeurs insérées ; les pluriels sont notés « un / autre ». Les noms d’appareils et de paramètres restent en anglais exprès.

| Écran | Textes |
|---|---|
| Menus et barre d’outils | 201 |
| Réglages | 537 |
| Accueil, À propos, Nouveautés | 25 |
| Palette de commandes | 92 |
| Navigateur | 90 |
| Navigateur et modèles de vue | 24 |
| Arrangement et pistes | 109 |
| Session | 153 |
| Mixeur | 20 |
| Modulaire | 96 |
| Éditeurs (piano roll, clips) | 115 |
| Outils MIDI (piano roll) | 114 |
| Appareils (cartes et fenêtres) | 1283 |
| Catalogue d’appareils et catégories | 114 |
| Dialogues, fichiers et messages d’état | 344 |
| Messages des couches basses (téléchargements, mises à jour, aperçus, erreurs) | 70 |
| Application (libellés partagés) | 2 |
| Autres | 89 |
| **Total** | **3478** |


## Menus et barre d’outils (201)

| English | Français | Source |
|---|---|---|
| Downloading Nota {0}… | Téléchargement de Nota {0}… | AppUpdates.cs |
| Couldn't download the update: {0} | Impossible de télécharger la mise à jour : {0} | AppUpdates.cs |
| Hide {0} | Masquer {0} | MacAppMenu.cs |
| Hide Others | Masquer les autres | MacAppMenu.cs |
| Show All | Tout afficher | MacAppMenu.cs |
| Quit {0} | Quitter {0} | MacAppMenu.cs |
| Not implemented yet. | Pas encore disponible. | MainWindow.Menu.cs |
| Nothing to undo. | Rien à annuler. | MainWindow.Menu.cs |
| Nothing to redo. | Rien à rétablir. | MainWindow.Menu.cs |
| Scenes live in the Session view. | Les scènes se trouvent dans la vue Session. | MainWindow.Menu.cs |
| Duplicated notes | Notes dupliquées | MainWindow.Menu.cs |
| Duplicated track(s) | Piste(s) dupliquée(s) | MainWindow.Menu.cs |
| Duplicated automation | Automation dupliquée | MainWindow.Menu.cs |
| Duplicated range | Plage dupliquée | MainWindow.Menu.cs |
| Duplicated clip(s) | Clip(s) dupliqué(s) | MainWindow.Menu.cs |
| Select a clip first. | Sélectionner d’abord un clip. | MainWindow.Menu.cs |
| Select a time range first. | Sélectionner d’abord une plage de temps. | MainWindow.Menu.cs |
| Duplicated time | Temps dupliqué | MainWindow.Menu.cs |
| Nothing to duplicate in the selected range. | Rien à dupliquer dans la plage sélectionnée. | MainWindow.Menu.cs |
| Inserted silence | Silence inséré | MainWindow.Menu.cs |
| Nothing after the selection to move. | Rien à déplacer après la sélection. | MainWindow.Menu.cs |
| Split at selection | Découpé à la sélection | MainWindow.Menu.cs |
| Split clip(s) at playhead | Clip(s) découpé(s) à la tête de lecture | MainWindow.Menu.cs |
| Select a range, or a clip the playhead crosses. | Sélectionner une plage, ou un clip que la tête de lecture traverse. | MainWindow.Menu.cs |
| Consolidated | Consolidé | MainWindow.Menu.cs |
| Select a range or clips to consolidate. | Sélectionner une plage ou des clips à consolider. | MainWindow.Menu.cs |
| Copied track(s) | Piste(s) copiée(s) | MainWindow.Menu.cs |
| Copied notes | Notes copiées | MainWindow.Menu.cs |
| Copied range | Plage copiée | MainWindow.Menu.cs |
| Copied clip | Clip copié | MainWindow.Menu.cs |
| Cut track(s) | Piste(s) coupée(s) | MainWindow.Menu.cs |
| Cut notes | Notes coupées | MainWindow.Menu.cs |
| Cut range | Plage coupée | MainWindow.Menu.cs |
| Cut clip | Clip coupé | MainWindow.Menu.cs |
| Pasted track(s) | Piste(s) collée(s) | MainWindow.Menu.cs |
| Pasted notes | Notes collées | MainWindow.Menu.cs |
| Pasted clip | Clip collé | MainWindow.Menu.cs |
| Select a clip or a scene to copy to the arrangement. | Sélectionner un clip ou une scène à copier dans l’arrangement. | MainWindow.Menu.cs |
| Select clips to copy to the session. | Sélectionner des clips à copier dans la session. | MainWindow.Menu.cs |
| Deleted track(s) | Piste(s) supprimée(s) | MainWindow.Menu.cs |
| Deleted notes | Notes supprimées | MainWindow.Menu.cs |
| Deleted clip(s) | Clip(s) supprimé(s) | MainWindow.Menu.cs |
| Overview strip hidden | Bandeau d’aperçu masqué | MainWindow.Menu.cs |
| Overview strip shown | Bandeau d’aperçu affiché | MainWindow.Menu.cs |
| Sections lane hidden | Couloir des sections masqué | MainWindow.Menu.cs |
| Sections lane shown | Couloir des sections affiché | MainWindow.Menu.cs |
| Clip names: every clip | Noms des clips : tous les clips | MainWindow.Menu.cs |
| Clip names: first of a run | Noms des clips : premier d’une série | MainWindow.Menu.cs |
| Clip names: none | Noms des clips : aucun | MainWindow.Menu.cs |
| Loop set to selection | Boucle réglée sur la sélection | MainWindow.Menu.cs |
| Show parameter-automation lanes (⌘⇧A) | Afficher les couloirs d’automation des paramètres (⌘⇧A) | MainWindow.MenuKeys.cs |
| About Nota | À propos de Nota | MainWindow.axaml |
| Check for updates… | Rechercher des mises à jour… | MainWindow.axaml |
| Settings… | Réglages… | MainWindow.axaml |
| Redo | Rétablir | MainWindow.axaml |
| New | Nouveau | MainWindow.axaml |
| Open… | Ouvrir… | MainWindow.axaml |
| Save as… | Enregistrer sous… | MainWindow.axaml |
| Save version with note… | Enregistrer une version avec note… | MainWindow.axaml |
| Import audio… | Importer de l’audio… | MainWindow.axaml |
| Export audio… | Exporter de l’audio… | MainWindow.axaml |
| Exit | Quitter | MainWindow.axaml |
| Edit | Édition | MainWindow.axaml |
| Duplicate clip | Dupliquer le clip | MainWindow.axaml |
| Insert scene | Insérer une scène | MainWindow.axaml |
| Split at playhead | Découper à la tête de lecture | MainWindow.axaml |
| Consolidate | Consolider | MainWindow.axaml |
| Duplicate time | Dupliquer le temps | MainWindow.axaml |
| Insert silence | Insérer du silence | MainWindow.axaml |
| Lock envelopes | Verrouiller les enveloppes | MainWindow.axaml |
| Paste bounced audio | Coller l’audio rendu | MainWindow.axaml |
| Copy clips to Session / Arrangement | Copier les clips vers Session / Arrangement | MainWindow.axaml |
| Narrow grid | Réduire la grille | MainWindow.axaml |
| Widen grid | Élargir la grille | MainWindow.axaml |
| Triplet grid | Grille en triolets | MainWindow.axaml |
| Adaptive grid | Grille adaptative | MainWindow.axaml |
| View | Affichage | MainWindow.axaml |
| Command palette… | Palette de commandes… | MainWindow.axaml |
| Mixer | Mixeur | MainWindow.axaml |
| Mixer in its own window | Mixeur dans sa propre fenêtre | MainWindow.axaml |
| Connect phone… | Connecter un téléphone… | MainWindow.axaml |
| Toggle browser | Afficher/masquer le navigateur | MainWindow.axaml |
| Toggle detail panel | Afficher/masquer le panneau de détail | MainWindow.axaml |
| Clip editor fills the window | L’éditeur de clips remplit la fenêtre | MainWindow.axaml |
| Toggle overview | Afficher/masquer l’aperçu | MainWindow.axaml |
| Toggle sections | Afficher/masquer les sections | MainWindow.axaml |
| Toggle full screen | Activer/quitter le plein écran | MainWindow.axaml |
| Follow playhead | Suivre la tête de lecture | MainWindow.axaml |
| Show playhead | Afficher la tête de lecture | MainWindow.axaml |
| Clip names | Noms des clips | MainWindow.axaml |
| Play / stop | Lecture / arrêt | MainWindow.axaml |
| Play / pause | Lecture / pause | MainWindow.axaml |
| Auto-arm selected track | Armement automatique de la piste sélectionnée | MainWindow.axaml |
| Loop | Boucle | MainWindow.axaml |
| Loop record: overdub / replace | Enregistrement en boucle : superposition / remplacement | MainWindow.axaml |
| Help | Aide | MainWindow.axaml |
| Documentation | Documentation | MainWindow.axaml |
| What's new | Nouveautés | MainWindow.axaml |
| Copy | Copier | MainWindow.axaml |
| Cut | Couper | MainWindow.axaml |
| Paste | Coller | MainWindow.axaml |
| Remote | Télécommande | MainWindow.axaml |
| File | Fichier | MainWindow.axaml |
| Transport | Transport | MainWindow.axaml |
| Record | Enregistrement | MainWindow.axaml |
| Metronome | Métronome | MainWindow.axaml |
| Save | Enregistrer | MainWindow.axaml |
| Arrangement | Arrangement | MainWindow.axaml |
| Stop (Return) | Arrêter (Return) | MainWindow.axaml |
| Play (Space) | Lire (Space) | MainWindow.axaml |
| Record (⌘R) | Lancer l’enregistrement (⌘R) | MainWindow.axaml |
| Switch between bars and time | Basculer entre mesures et temps | MainWindow.axaml |
| Loop the range set on the ruler | Mettre en boucle la plage définie sur la règle | MainWindow.axaml |
| Loop recording: Overdub adds every pass · Replace keeps the latest pass (⌘⇧R) | Enregistrement en boucle : Superposer ajoute chaque passage · Remplacer garde le dernier passage (⌘⇧R) | MainWindow.axaml |
| Overdub | Superposer | MainWindow.axaml |
| Replace | Remplacer | MainWindow.axaml |
| Set the tempo — drag, or double-click to type | Régler le tempo : glisser, ou double-clic pour le saisir | MainWindow.axaml |
| Tap the beat to set the tempo | Taper le rythme pour régler le tempo | MainWindow.axaml |
| Tap | Tap | MainWindow.axaml |
| Set the time signature — drag each field | Régler la signature rythmique : glisser chaque champ | MainWindow.axaml |
| Set the project key — samples can be transposed to it | Régler la tonalité du projet : les échantillons peuvent y être transposés | MainWindow.axaml |
| Key | Tonalité | MainWindow.axaml |
| Turn the metronome on or off (⌘M) · right-click for count-in | Activer ou désactiver le métronome (⌘M) · clic droit pour le décompte | MainWindow.axaml |
| Snap clips to the grid (hold Alt to drag freely) | Caler les clips sur la grille (maintenir Alt pour déplacer librement) | MainWindow.axaml |
| Re-enable | Réactiver | MainWindow.axaml |
| Hand overridden controls back to their automation | Rendre les commandes modifiées à leur automation | MainWindow.axaml |
| Map a controller — click a control, then move a knob on the controller | Assigner un contrôleur : cliquer sur une commande, puis bouger un bouton du contrôleur | MainWindow.axaml |
| Play and control Nota from a phone | Jouer et piloter Nota depuis un téléphone | MainWindow.axaml |
| Sample rate and buffer size. Click to change the buffer size | Fréquence d’échantillonnage et taille du tampon. Cliquer pour changer la taille du tampon | MainWindow.axaml |
| View release notes | Voir les notes de version | MainWindow.axaml |
| Strips | Tranches | MainWindow.axaml |
| Stereo map | Carte stéréo | MainWindow.axaml |
| Open the mixer in a window | Ouvrir le mixeur dans une fenêtre | MainWindow.axaml |
| Hide the mixer | Masquer le mixeur | MainWindow.axaml |
| Pattern | Motif | MainWindow.axaml |
| Step the selected MIDI clip on a Drum Rack track | Programmer pas à pas le clip MIDI sélectionné d’une piste Drum Rack | MainWindow.axaml |
| Clip | Clip | MainWindow.axaml |
| Select a clip to edit it | Sélectionner un clip pour l’éditer | MainWindow.axaml |
| Freeze the track to audio to free CPU (right-click to flatten) | Geler la piste en audio pour libérer du CPU (clic droit pour aplatir) | MainWindow.axaml |
| Freeze | Geler | MainWindow.axaml |
| Bounce to a linked audio track below and put the source to sleep | Rendre vers une piste audio liée en dessous et mettre la source en veille | MainWindow.axaml |
| Live freeze | Gel en direct | MainWindow.axaml |
| Wake the frozen source to edit it (right-click to unfreeze or flatten) | Réveiller la source gelée pour l’éditer (clic droit pour dégeler ou aplatir) | MainWindow.axaml |
| Re-freeze the source and put it back to sleep | Regeler la source et la remettre en veille | MainWindow.axaml |
| Done | Terminé | MainWindow.axaml |
| Leave edit mode without re-freezing | Quitter le mode édition sans regeler | MainWindow.axaml |
| Open devices and clip in a separate window | Ouvrir les appareils et le clip dans une fenêtre séparée | MainWindow.axaml |
| Scroll the arrangement to keep the playhead in view | Faire défiler l’arrangement pour garder la tête de lecture visible | MainWindow.axaml |
| Session | Session | MainWindow.axaml |
| Modular | Modular | MainWindow.axaml |
| Off\|remote | Off | MainWindow.axaml |
| Play | Lecture | MainWindow.axaml.cs |
| Convert to MIDI | Convertir en MIDI | MainWindow.axaml.cs |
| Convert Melody and Convert Harmony now use the AI model. | « Convertir la mélodie » et « Convertir l’harmonie » utilisent désormais le modèle d’IA. | MainWindow.axaml.cs |
| Master volume | Volume master | MainWindow.axaml.cs |
| Master Volume | Volume master | MainWindow.axaml.cs |
| DSP {0}% | DSP {0}% | MainWindow.axaml.cs |
| Bass | Basse | MainWindow.axaml.cs |
| Drums | Batterie | MainWindow.axaml.cs |
| Pad | Pad | MainWindow.axaml.cs |
| Instrument track {0} (synth) | Piste d’instrument {0} (synthé) | MainWindow.axaml.cs |
| Audio track {0} — arm it and hit Rec to record input | Piste audio {0} — l’armer puis appuyer sur Rec pour enregistrer l’entrée | MainWindow.axaml.cs |
| All return buses are in use. | Tous les bus de retour sont utilisés. | MainWindow.axaml.cs |
| Return {0} added — add an effect, then dial up track sends | Retour {0} ajouté — ajouter un effet, puis augmenter les envois des pistes | MainWindow.axaml.cs |
| No key | Aucune tonalité | MainWindow.axaml.cs |
| No project key | Aucune tonalité de projet | MainWindow.axaml.cs |
| Project key: {0} | Tonalité du projet : {0} | MainWindow.axaml.cs |
| measuring… | mesure… | MainWindow.axaml.cs |
| DSP load: {0} of the audio buffer time — dropouts start near 100 % | Charge DSP : {0} du temps de tampon audio — les décrochages commencent près de 100 % | MainWindow.axaml.cs |
| Nota process CPU: {0} (audio, interface, plugins) | CPU du processus Nota : {0} (audio, interface, plug-ins) | MainWindow.axaml.cs |
| Tempo: {0} BPM (tapped) | Tempo : {0} BPM (tapé) | MainWindow.axaml.cs |
| Follow playhead off | Suivi de la tête de lecture désactivé | MainWindow.axaml.cs |
| Following the playhead | Suivi de la tête de lecture | MainWindow.axaml.cs |
| Note audition off | Écoute des notes désactivée | MainWindow.axaml.cs |
| Note audition on | Écoute des notes activée | MainWindow.axaml.cs |
| Follow is paused while you look elsewhere · P or a click here picks it up again | Le suivi est en pause pendant la consultation d’un autre endroit · P ou un clic ici le reprend | MainWindow.axaml.cs |
| Follow paused · press P to go back to the playhead | Suivi en pause · appuyer sur P pour revenir à la tête de lecture | MainWindow.axaml.cs |
| Snap off — clips position freely | Magnétisme désactivé — les clips se placent librement | MainWindow.axaml.cs |
| Snap on | Magnétisme activé | MainWindow.axaml.cs |
| Recent | Récents | MenuKit.Rows.cs |
| Reveal in Finder | Afficher dans le Finder | MenuKit.cs |
| Show in Explorer | Afficher dans l’Explorateur | MenuKit.cs |
| Show in file manager | Afficher dans le gestionnaire de fichiers | MenuKit.cs |
| Grid | Grille | SnapChip.cs |
| Snap to grid | Magnétisme sur la grille | SnapChip.cs |
| Auto · {0} | Auto · {0} | SnapChip.cs |
| Auto grid | Grille auto | SnapChip.cs |
| Snap off — items move freely (⌘4) | Magnétisme désactivé — les éléments se déplacent librement (⌘4) | SnapChip.cs |
| {0} — finer than the visible grid · zoom in to see it | {0} — plus fin que la grille visible · zoomer pour la voir | SnapChip.cs |
| Snap grid · Auto follows the zoom ({0}) | Grille magnétique · Auto suit le zoom ({0}) | SnapChip.cs |
| Snap grid · fixed {0} | Grille magnétique · fixe {0} | SnapChip.cs |
| {0}⏎ {1} · hold ⌥ to drag freely | {0}⏎ {1} · maintenir ⌥ pour déplacer librement | SnapChip.cs |
| Triplets | Triolets | SnapChip.cs |
| ⌘1 ⌘2 narrow / widen · ⌘3 triplets · ⌘4 snap · ⌘5 auto | ⌘1 ⌘2 rétrécir / élargir · ⌘3 triolets · ⌘4 magnétisme · ⌘5 auto | SnapChip.cs |
| Can't record: arm a track first (click Arm on a track header). | Enregistrement impossible : armer d’abord une piste (cliquer sur Armer dans l’en-tête d’une piste). | TransportViewModel.cs |
| Can't record: no audio input — grant microphone access (needs a bundled app). | Enregistrement impossible : aucune entrée audio — autoriser l’accès au microphone (nécessite une app empaquetée). | TransportViewModel.cs |
| Recording didn't start. | L’enregistrement n’a pas démarré. | TransportViewModel.cs |
| Paused · Play carries on from here | En pause · la lecture reprend à partir d’ici | TransportViewModel.cs |
| bars | mesures | TransportViewModel.cs |
| time | durée | TransportViewModel.cs |
| count-in | décompte | TransportViewModel.cs |

## Réglages (537)

| English | Français | Source |
|---|---|---|
| AI models | Modèles d’IA | PreferencesWindow.Downloads.cs |
| Sample packs | Packs d’échantillons | PreferencesWindow.Downloads.cs |
| Plug-ins | Plug-ins | PreferencesWindow.Downloads.cs |
| Applies after restart | S’applique au redémarrage | PreferencesWindow.Language.cs |
| Restart | Redémarrer | PreferencesWindow.Language.cs |
| Applies the next time Nota starts | S’applique au prochain démarrage de Nota | PreferencesWindow.Language.cs |
| AI models that run on this computer: the audio you give them never leaves it. Each downloads once from where its authors publish it and is checked against the checksum Nota pins for it. | Modèles d’IA exécutés sur cet ordinateur : l’audio traité ne le quitte jamais. Chacun est téléchargé une seule fois depuis le site de ses auteurs, puis vérifié avec la somme de contrôle que Nota lui associe. | PreferencesWindow.Models.cs |
| ONNX Runtime has no build for this computer, so the models can't run here. | ONNX Runtime n’existe pas pour cet ordinateur : les modèles ne peuvent pas s’y exécuter. | PreferencesWindow.Models.cs |
| Removing… | Suppression… | PreferencesWindow.Models.cs |
| Comes with a model | Fourni avec un modèle | PreferencesWindow.Models.cs |
| On disk | Sur le disque | PreferencesWindow.Models.cs |
| {0} model + {1} AI runtime, downloaded once | Modèle de {0} + environnement d’exécution IA de {1}, téléchargés une seule fois | PreferencesWindow.Models.cs |
| Download | Télécharger | PreferencesWindow.Models.cs |
| Open the model's project page | Ouvrir la page du projet du modèle | PreferencesWindow.Models.cs |
| {0} is installed — right-click an audio clip and choose Separate Stems. | {0} est installé : clic droit sur un clip audio, puis « Séparer les stems ». | PreferencesWindow.Models.cs |
| {0} is installed — Convert Melody and Convert Harmony now use it. | {0} est installé : « Convertir la mélodie » et « Convertir l’harmonie » l’utilisent désormais. | PreferencesWindow.Models.cs |
| Remove AI model | Supprimer le modèle d’IA | PreferencesWindow.Models.cs |
| Remove {0}? {1} will offer to download it again. | Supprimer {0} ? {1} proposera de le télécharger à nouveau. | PreferencesWindow.Models.cs |
|  The AI runtime goes too. |  Le moteur d’IA est supprimé aussi. | PreferencesWindow.Models.cs |
| Pads and keys. Mixer, transport and mappings are read-only. | Pads et touches. Le mixeur, le transport et les assignations sont en lecture seule. | PreferencesWindow.Remote.cs |
| Play notes only | Jouer des notes uniquement | PreferencesWindow.Remote.cs |
| Also mixer, transport, macros, XY and Session. | Aussi le mixeur, le transport, les macros, le XY et la vue Session. | PreferencesWindow.Remote.cs |
| Play and control the project | Jouer et piloter le projet | PreferencesWindow.Remote.cs |
| No phone has paired yet. Turn Remote on and scan the code in Connect phone. | Aucun téléphone n’est encore associé. Activer la télécommande et scanner le code dans « Connecter un téléphone ». | PreferencesWindow.Remote.cs |
| now | maintenant | PreferencesWindow.Remote.cs |
| Forget | Oublier | PreferencesWindow.Remote.cs |
| Disconnect it and make it scan a new code to come back | Le déconnecter et lui faire scanner un nouveau code pour revenir | PreferencesWindow.Remote.cs |
| On · no network | Activée · pas de réseau | PreferencesWindow.Remote.cs |
| USB  | USB  | PreferencesWindow.Remote.cs |
| Allow control from a phone | Autoriser le contrôle depuis un téléphone | PreferencesWindow.Remote.cs |
| Port | Port | PreferencesWindow.Remote.cs |
| Status | État | PreferencesWindow.Remote.cs |
| Phones may | Les téléphones peuvent | PreferencesWindow.Remote.cs |
| When you turn this on, macOS asks whether Nota may find devices on the local network. Allow it, or phones can't reach Nota. | À l’activation, macOS demande si Nota peut trouver des appareils sur le réseau local. Il faut l’autoriser, sinon les téléphones ne peuvent pas joindre Nota. | PreferencesWindow.Remote.cs |
| When you turn this on, Windows asks whether Nota may use the network. Allow private networks, or phones can't reach Nota. | À l’activation, Windows demande si Nota peut utiliser le réseau. Il faut autoriser les réseaux privés, sinon les téléphones ne peuvent pas joindre Nota. | PreferencesWindow.Remote.cs |
| Phones reach Nota on the port above: if a firewall is on, allow it. | Les téléphones joignent Nota sur le port ci-dessus : si un pare-feu est actif, l’autoriser. | PreferencesWindow.Remote.cs |
| Nota Remote | Nota Remote | PreferencesWindow.Remote.cs |
| Trusted devices | Appareils de confiance | PreferencesWindow.Remote.cs |
| just now | à l’instant | PreferencesWindow.Remote.cs |
| {0} min ago | il y a {0} min | PreferencesWindow.Remote.cs |
| {0} h ago | il y a {0} h | PreferencesWindow.Remote.cs |
| {0} days ago | il y a {0} jours | PreferencesWindow.Remote.cs |
|  Nota never shows files or paths to the phone, and serves nothing from your disk. |  Nota n’affiche jamais de fichiers ni de chemins sur le téléphone, et ne sert rien depuis le disque. | PreferencesWindow.Remote.cs |
| A trusted phone reconnects from its Home Screen icon without a code. Forget it and it has to scan again. Turning Remote off disconnects everyone. | Un téléphone de confiance se reconnecte depuis l’icône de son écran d’accueil, sans code. L’oublier l’oblige à scanner de nouveau. Désactiver la télécommande déconnecte tout le monde. | PreferencesWindow.Remote.cs |
| Listening on {0} | Écoute sur {0} | PreferencesWindow.Remote.cs |
| Free sample packs from the Nota sample registry, licensed for any music, commercial releases included. Each pack downloads from where its author publishes it, is checked against the registry's checksum and unpacked into your Samples folder. | Packs d’échantillons gratuits issus du registre d’échantillons de Nota, utilisables pour toute musique, sorties commerciales comprises. Chaque pack est téléchargé depuis le site de son auteur, vérifié avec la somme de contrôle du registre, puis décompressé dans le dossier d’échantillons. | PreferencesWindow.Samples.cs |
| No sample packs installed from the registry yet. | Aucun pack d’échantillons installé depuis le registre pour l’instant. | PreferencesWindow.Samples.cs |
| Loading the sample registry… | Chargement du registre d’échantillons… | PreferencesWindow.Samples.cs |
| Loops | Boucles | PreferencesWindow.Samples.cs |
| Mixed | Mixte | PreferencesWindow.Samples.cs |
| Credit: {0} | Crédit : {0} | PreferencesWindow.Samples.cs |
| Show the pack's folder | Afficher le dossier du pack | PreferencesWindow.Samples.cs |
| {0} samples | {0} échantillon / {0} échantillons | PreferencesWindow.Samples.cs |
| {0} · {1} samples | {0} · {1} échantillons | PreferencesWindow.Samples.cs |
| {0} download · {1} samples · {2} on disk · {3} | {0} à télécharger · {1} échantillons · {2} sur le disque · {3} | PreferencesWindow.Samples.cs |
| Open the pack's page | Ouvrir la page du pack | PreferencesWindow.Samples.cs |
| {0} is installed — find it under Downloaded in the browser's Files tab, where it is being analysed for tempo and key; its instruments appear under Nota Mosaic → Packs. | {0} est installé : il se trouve sous « Téléchargés » dans l’onglet Fichiers du navigateur, où son tempo et sa tonalité sont en cours d’analyse ; ses instruments apparaissent sous Nota Mosaic → Packs. | PreferencesWindow.Samples.cs |
| Remove sample pack | Supprimer le pack d’échantillons | PreferencesWindow.Samples.cs |
| Remove {0}? Its folder ({1}) is deleted. | Supprimer {0} ? Son dossier ({1}) sera supprimé. | PreferencesWindow.Samples.cs |
| One-shots | One-shots | PreferencesWindow.Samples.cs |
| Installing… | Installation… | PreferencesWindow.Store.cs |
| Installed | Installé | PreferencesWindow.Store.cs |
| Remove | Supprimer | PreferencesWindow.Store.cs |
| Install | Installer | PreferencesWindow.Store.cs |
| Source | Source | PreferencesWindow.Store.cs |
| Downloading {0}… | Téléchargement de {0}… | PreferencesWindow.Store.cs |
| Removing {0}… | Suppression de {0}… | PreferencesWindow.Store.cs |
| {0} was removed. | {0} a été supprimé. | PreferencesWindow.Store.cs |
| All | Tous | PreferencesWindow.Store.cs |
| Instruments | Instruments | PreferencesWindow.Store.cs |
| Search | Rechercher | PreferencesWindow.Store.cs |
| Refresh | Actualiser | PreferencesWindow.Store.cs |
| Reload the registry | Recharger le registre | PreferencesWindow.Store.cs |
| Installed to {0} | Installé dans {0} | PreferencesWindow.Store.cs |
| Refreshing… | Actualisation… | PreferencesWindow.Store.cs |
| Nothing matches. | Aucun résultat. | PreferencesWindow.Store.cs |
| Instrument | Instrument | PreferencesWindow.Store.cs |
| Working… | En cours… | PreferencesWindow.Store.cs |
| Update to {0} | Mettre à jour vers {0} | PreferencesWindow.Store.cs |
| Effects | Effets | PreferencesWindow.Store.cs |
| Open-source VST3 plugins from the Nota plugin registry. Each one downloads from the project's own GitHub release, is checked against the registry's checksum and unpacked — installers never run. | Plug-ins VST3 open source issus du registre de plug-ins de Nota. Chacun est téléchargé depuis la publication GitHub du projet, vérifié avec la somme de contrôle du registre, puis décompressé : aucun installeur n’est jamais exécuté. | PreferencesWindow.Store.cs |
| Loading the plugin registry… | Chargement du registre de plug-ins… | PreferencesWindow.Store.cs |
| Linux | Linux | PreferencesWindow.Store.cs |
| Windows | Windows | PreferencesWindow.Store.cs |
| Nothing installed from the registry yet. | Rien d’installé depuis le registre pour l’instant. | PreferencesWindow.Store.cs |
| Not available for this computer | Indisponibles pour cet ordinateur | PreferencesWindow.Store.cs |
| Stop the download — nothing is changed | Arrêter le téléchargement : rien n’est modifié | PreferencesWindow.Store.cs |
| Open the source repository | Ouvrir le dépôt source | PreferencesWindow.Store.cs |
| No {0} version | Pas de version {0} | PreferencesWindow.Store.cs |
| No {0} version for {1} | Pas de version {0} pour {1} | PreferencesWindow.Store.cs |
| Apple silicon + Intel | Apple silicon + Intel | PreferencesWindow.Store.cs |
| Apple silicon | Apple silicon | PreferencesWindow.Store.cs |
| Effect | Effet | PreferencesWindow.Store.cs |
| Collection | Collection | PreferencesWindow.Store.cs |
| Scanning {0}… | Analyse de {0}… | PreferencesWindow.Store.cs |
| {0} {1} is installed — find it in the browser. | {0} {1} est installé : il se trouve dans le navigateur. | PreferencesWindow.Store.cs |
| {0} is installed. Rescan plugins to use it. | {0} est installé. Relancer l’analyse des plug-ins pour l’utiliser. | PreferencesWindow.Store.cs |
| Remove plugin | Supprimer le plug-in | PreferencesWindow.Store.cs |
| Remove {0}? Projects that use it will load without it until it's installed again. | Supprimer {0} ? Les projets qui l’utilisent se chargeront sans lui tant qu’il ne sera pas réinstallé. | PreferencesWindow.Store.cs |
| Check for updates automatically | Rechercher les mises à jour automatiquement | PreferencesWindow.Updates.cs |
| Check now | Rechercher maintenant | PreferencesWindow.Updates.cs |
| Checking… | Recherche… | PreferencesWindow.Updates.cs |
| Nota {0} is available — you have {1}. | Nota {0} est disponible : la version installée est la {1}. | PreferencesWindow.Updates.cs |
| Download opens the release page. | « Télécharger » ouvre la page de la version. | PreferencesWindow.Updates.cs |
| Update downloads it here; it installs when you restart or quit Nota. | « Mettre à jour » la télécharge ici ; elle s’installe au redémarrage ou à la fermeture de Nota. | PreferencesWindow.Updates.cs |
| It installs the next time you quit Nota, or click Restart to update in the status bar. | Elle s’installe à la prochaine fermeture de Nota, ou en cliquant sur « Redémarrer pour mettre à jour » dans la barre d’état. | PreferencesWindow.Updates.cs |
| Nota {0} is ready to install. | Nota {0} est prêt à être installé. | PreferencesWindow.Updates.cs |
| The update didn't download. | La mise à jour n’a pas pu être téléchargée. | PreferencesWindow.Updates.cs |
| Couldn't reach GitHub to check for a newer version. Check your internet connection and try again. | Impossible de joindre GitHub pour rechercher une nouvelle version. Vérifier la connexion Internet, puis réessayer. | PreferencesWindow.Updates.cs |
| You have Nota {0}. | La version installée de Nota est la {0}. | PreferencesWindow.Updates.cs |
| Last checked {0}. | Dernière recherche : {0}. | PreferencesWindow.Updates.cs |
| Nota {0} is the latest version. | Nota {0} est la dernière version. | PreferencesWindow.Updates.cs |
| Not checked yet. | Pas encore recherchée. | PreferencesWindow.Updates.cs |
| This copy of Nota can't replace itself (a development build, or one run straight from the disk image), so a new version opens its release page to download. | Cet exemplaire de Nota ne peut pas se remplacer lui-même (version de développement, ou lancée directement depuis l’image disque) : une nouvelle version ouvre donc sa page de publication pour être téléchargée. | PreferencesWindow.Updates.cs |
| Automatic updates | Mises à jour automatiques | PreferencesWindow.Updates.cs |
| On launch and once a day, Nota asks GitHub whether a newer version is out and tells you in the status bar. Nothing is downloaded until you click Update. | Au lancement, puis une fois par jour, Nota demande à GitHub si une version plus récente est disponible et l’indique dans la barre d’état. Rien n’est téléchargé avant un clic sur « Mettre à jour ». | PreferencesWindow.Updates.cs |
| This version | Cette version | PreferencesWindow.Updates.cs |
| Language | Langue | PreferencesWindow.cs |
| Device, sample rate and buffer | Périphérique, fréquence d’échantillonnage et mémoire tampon | PreferencesWindow.cs |
| Controllers Nota listens to | Contrôleurs écoutés par Nota | PreferencesWindow.cs |
| Gamepads | Manettes | PreferencesWindow.cs |
| Play notes or drive mapped controls | Jouer des notes ou piloter les commandes assignées | PreferencesWindow.cs |
| Phones and tablets on this network | Téléphones et tablettes sur ce réseau | PreferencesWindow.cs |
| Where Nota looks for VST3 | Où Nota cherche les VST3 | PreferencesWindow.cs |
| Downloads | Téléchargements | PreferencesWindow.cs |
| Plug-ins, sample packs and AI models | Plug-ins, packs d’échantillons et modèles d’IA | PreferencesWindow.cs |
| General | Général | PreferencesWindow.cs |
| Content folders and version history | Dossiers de contenu et historique des versions | PreferencesWindow.cs |
| Library | Bibliothèque | PreferencesWindow.cs |
| Appearance | Apparence | PreferencesWindow.cs |
| Theme, scale, waveforms and AI control | Thème, échelle, formes d’onde et contrôle par IA | PreferencesWindow.cs |
| Keyboard reference | Référence du clavier | PreferencesWindow.cs |
| Shortcuts | Raccourcis | PreferencesWindow.cs |
| New versions of Nota | Nouvelles versions de Nota | PreferencesWindow.cs |
| Updates | Mises à jour | PreferencesWindow.cs |
| Audio settings are unavailable in this window. | Les réglages audio ne sont pas disponibles dans cette fenêtre. | PreferencesWindow.cs |
| Latency | Latence | PreferencesWindow.cs |
| Audio device | Périphérique audio | PreferencesWindow.cs |
| Test | Test | PreferencesWindow.cs |
| Exclusive mode | Mode exclusif | PreferencesWindow.cs |
| WASAPI exclusive — lowest latency, mutes other apps | WASAPI exclusif : latence minimale, coupe le son des autres applications | PreferencesWindow.cs |
| The device's native sample rate and buffer size are used instead of the ones above. If it is busy or doesn't allow exclusive access, Nota falls back to shared mode. | La fréquence d’échantillonnage et la taille de tampon natives du périphérique remplacent celles ci-dessus. Si le périphérique est occupé ou n’autorise pas l’accès exclusif, Nota revient au mode partagé. | PreferencesWindow.cs |
| ASIO | ASIO | PreferencesWindow.cs |
| Audio system | Système audio | PreferencesWindow.cs |
| No ASIO driver found. Few interfaces ship an ARM64 ASIO driver yet — install one from your interface's maker, or a generic one such as FlexASIO. | Aucun pilote ASIO trouvé. Peu d’interfaces proposent déjà un pilote ASIO ARM64 : en installer un auprès du fabricant de l’interface, ou un pilote générique comme FlexASIO. | PreferencesWindow.cs |
| No ASIO driver found. Install your interface's driver, or a generic one such as FlexASIO. | Aucun pilote ASIO trouvé. Installer le pilote de l’interface, ou un pilote générique comme FlexASIO. | PreferencesWindow.cs |
| Saved driver not found | Pilote enregistré introuvable | PreferencesWindow.cs |
| Driver | Pilote | PreferencesWindow.cs |
| No inputs on this driver | Aucune entrée sur ce pilote | PreferencesWindow.cs |
| The driver doesn't support {0} | Le pilote ne prend pas en charge {0} | PreferencesWindow.cs |
| {0} frames (set in the driver's settings) | {0} échantillons (défini dans les réglages du pilote) | PreferencesWindow.cs |
| Set in the driver's settings | Défini dans les réglages du pilote | PreferencesWindow.cs |
| Play test tone | Jouer une tonalité de test | PreferencesWindow.cs |
| {0:0} Hz sine at −14 dBFS, through the master. | Sinus de {0:0} Hz à −14 dBFS, via le master. | PreferencesWindow.cs |
| Stop tone | Arrêter la tonalité | PreferencesWindow.cs |
| Output check | Contrôle de la sortie | PreferencesWindow.cs |
| CPU check | Test du CPU | PreferencesWindow.cs |
| Measures audio-thread load for 5 s — play your project for a real-world figure. | Mesure la charge du thread audio pendant 5 s : lancer la lecture du projet pour obtenir une valeur réaliste. | PreferencesWindow.cs |
| Audio device not running. | Le périphérique audio n’est pas démarré. | PreferencesWindow.cs |
| Measuring… | Mesure… | PreferencesWindow.cs |
| Measuring… {0} s left · now {1:0} % | Mesure… encore {0} s · actuellement {1:0} % | PreferencesWindow.cs |
| overloaded — raise the buffer size | surchargé : augmenter la taille du tampon | PreferencesWindow.cs |
| close to the limit — consider a larger buffer | proche de la limite : envisager un tampon plus grand | PreferencesWindow.cs |
| plenty of headroom | large marge | PreferencesWindow.cs |
| Last check: {0:0} % average, {1:0} % peak · {2} — {3}. | Dernier contrôle : {0:0} % en moyenne, {1:0} % au pic · {2} — {3}. | PreferencesWindow.cs |
| {0} dropouts | {0} décrochage / {0} décrochages | PreferencesWindow.cs |
| Measuring… 5 s left — play your project for a real-world figure. | Mesure… encore 5 s : lancer la lecture du projet pour obtenir une valeur réaliste. | PreferencesWindow.cs |
| Performance | Performances | PreferencesWindow.cs |
| ASIO unavailable ({0}) → WASAPI | ASIO indisponible ({0}) → WASAPI | PreferencesWindow.cs |
| the driver didn't start | le pilote ne s’est pas lancé | PreferencesWindow.cs |
|  · round trip {0} |  · aller-retour {0} | PreferencesWindow.cs |
| ASIO · {0} frames @ {1}{2} | ASIO · {0} échantillons @ {1}{2} | PreferencesWindow.cs |
| {0} frames @ {1}{2} | {0} échantillons @ {1}{2} | PreferencesWindow.cs |
| MIDI settings are unavailable in this window. | Les réglages MIDI ne sont pas disponibles dans cette fenêtre. | PreferencesWindow.cs |
| No MIDI inputs detected. Connect a controller and reopen Settings. | Aucune entrée MIDI détectée. Connecter un contrôleur, puis rouvrir les réglages. | PreferencesWindow.cs |
| Switch a controller off to stop Nota listening to it. New devices are on by default. | Désactiver un contrôleur pour que Nota cesse de l’écouter. Les nouveaux périphériques sont activés par défaut. | PreferencesWindow.cs |
| MIDI inputs | Entrées MIDI | PreferencesWindow.cs |
| ±{0} semitones | ±{0} demi-tons | PreferencesWindow.cs |
| Per-note expression from MPE controllers | Expression par note des contrôleurs MPE | PreferencesWindow.cs |
| Expression | Expression | PreferencesWindow.cs |
| Bend range | Amplitude du pitch bend | PreferencesWindow.cs |
| Gamepad settings are unavailable in this window. | Les réglages des manettes ne sont pas disponibles dans cette fenêtre. | PreferencesWindow.cs |
| Gamepad input is macOS-only in this build. | Les manettes ne sont prises en charge que sous macOS dans cette version. | PreferencesWindow.cs |
| Use a connected gamepad for notes and mapped controls | Utiliser une manette connectée pour les notes et les commandes assignées | PreferencesWindow.cs |
| No gamepad detected. Connect one and it appears here. | Aucune manette détectée. Une manette connectée apparaît ici. | PreferencesWindow.cs |
| Face buttons | Boutons de face | PreferencesWindow.cs |
| Shoulder buttons | Boutons d’épaule | PreferencesWindow.cs |
| Octave −1 / +1 | Octave −1 / +1 | PreferencesWindow.cs |
| Velocity − / + | Vélocité − / + | PreferencesWindow.cs |
| Gamepad input | Entrée manette | PreferencesWindow.cs |
| Layout | Disposition | PreferencesWindow.cs |
| Notes go to the armed (record-enabled) instrument track — enable Record to capture them, or use the audition track to just play. | Les notes vont à la piste d’instrument armée (prête à enregistrer) : activer l’enregistrement pour les capturer, ou utiliser la piste d’écoute pour simplement jouer. | PreferencesWindow.cs |
| Any button can be mapped to a control instead: click MIDI in the top-right, click the control, then press the button. A mapped button drives that control and stops playing its note; the rest of the pad keeps the layout above. The sticks and the trigger travel map the same way and are continuous, so they suit a knob or a fader — they play no notes, and do nothing until mapped. Mappings are listed in the browser's MIDI Map tab and travel with the project. | Un bouton quelconque peut être assigné à une commande à la place : cliquer sur MIDI en haut à droite, cliquer sur la commande, puis appuyer sur le bouton. Un bouton assigné pilote cette commande et ne joue plus sa note ; le reste de la manette garde la disposition ci-dessus. Les sticks et la course des gâchettes s’assignent de la même façon et sont continus, ce qui convient à un bouton rotatif ou à un fader : ils ne jouent aucune note et ne font rien tant qu’ils ne sont pas assignés. Les assignations sont listées dans l’onglet Carte MIDI du navigateur et suivent le projet. | PreferencesWindow.cs |
| Add folder… | Ajouter un dossier… | PreferencesWindow.cs |
| default | par défaut | PreferencesWindow.cs |
| Add plugin scan folder | Ajouter un dossier d’analyse de plug-ins | PreferencesWindow.cs |
| Rescan plugins | Relancer l’analyse des plug-ins | PreferencesWindow.cs |
| Plugin scanning is unavailable in this window. | L’analyse des plug-ins n’est pas disponible dans cette fenêtre. | PreferencesWindow.cs |
| Scanning… | Analyse… | PreferencesWindow.cs |
| Searching {0} folders | Recherche dans {0} dossier / Recherche dans {0} dossiers | PreferencesWindow.cs |
| Scanner worker not found. | Processus d’analyse introuvable. | PreferencesWindow.cs |
| Found {0:N0} plugins. | {0:N0} plug-in trouvé. / {0:N0} plug-ins trouvés. | PreferencesWindow.cs |
|  {0:N0} couldn't be loaded — see plugin-scan.log in Nota's logs folder. |  Impossible d’en charger {0:N0} : voir plugin-scan.log dans le dossier de journaux de Nota. | PreferencesWindow.cs |
| Open plug-in window on insert | Ouvrir la fenêtre du plug-in à l’insertion | PreferencesWindow.cs |
| Plug-in window | Fenêtre du plug-in | PreferencesWindow.cs |
| Adding a plug-in from the browser, by drag and drop or from the palette opens its editor. Loading a project, undo and paste never do. | L’ajout d’un plug-in depuis le navigateur, par glisser-déposer ou depuis la palette, ouvre son éditeur. Le chargement d’un projet, l’annulation et le collage ne l’ouvrent jamais. | PreferencesWindow.cs |
| Plugin scan folders | Dossiers d’analyse des plug-ins | PreferencesWindow.cs |
| Extra folders are searched for VST3 plugins on the next rescan (AU uses the system registry). | Des dossiers supplémentaires sont explorés à la recherche de plug-ins VST3 à la prochaine analyse (les AU utilisent le registre du système). | PreferencesWindow.cs |
| Scan | Analyse | PreferencesWindow.cs |
| Content folders are unavailable in this window. | Les dossiers de contenu ne sont pas disponibles dans cette fenêtre. | PreferencesWindow.cs |
| Content folders | Dossiers de contenu | PreferencesWindow.cs |
| Samples | Échantillons | PreferencesWindow.cs |
| Keep a version history of each project | Conserver un historique des versions de chaque projet | PreferencesWindow.cs |
| Turn off version history | Désactiver l’historique des versions | PreferencesWindow.cs |
| This erases the version history of the open project now, and of any other project the next time you save it. Older versions can't be brought back. | Cela efface dès maintenant l’historique des versions du projet ouvert, et celui de tout autre projet au prochain enregistrement. Les anciennes versions ne pourront pas être restaurées. | PreferencesWindow.cs |
| Turn off and erase | Désactiver et effacer | PreferencesWindow.cs |
| Version history | Historique des versions | PreferencesWindow.cs |
| Every save records a version, listed in the browser's History tab. Versions share their audio, so each one costs only what it adds. | Chaque enregistrement crée une version, listée dans l’onglet Historique du navigateur. Les versions partagent leur audio : chacune ne coûte que ce qu’elle ajoute. | PreferencesWindow.cs |
| custom | personnalisé | PreferencesWindow.cs |
| Choose… | Choisir… | PreferencesWindow.cs |
| Choose {0} folder | Choisir le dossier {0} | PreferencesWindow.cs |
| Dark | Sombre | PreferencesWindow.cs |
| Follow system | Suivre le système | PreferencesWindow.cs |
| Light | Clair | PreferencesWindow.cs |
| In dark mode | En mode sombre | PreferencesWindow.cs |
| In light mode | En mode clair | PreferencesWindow.cs |
| Solid | Plein | PreferencesWindow.cs |
| Left | Gauche | PreferencesWindow.cs |
| Right | Droite | PreferencesWindow.cs |
| Copy config | Copier la config | PreferencesWindow.cs |
| Add this config (or the URL) as an MCP server in Claude Desktop / Claude Code. Loopback only; off by default. | Ajouter cette config (ou l’URL) comme serveur MCP dans Claude Desktop / Claude Code. Boucle locale uniquement ; désactivé par défaut. | PreferencesWindow.cs |
| Listening on http://127.0.0.1:{0}/ (loopback) | Écoute sur http://127.0.0.1:{0}/ (boucle locale) | PreferencesWindow.cs |
| Enable MCP server (let an AI drive Nota) | Activer le serveur MCP (laisser une IA piloter Nota) | PreferencesWindow.cs |
| Copied | Copié | PreferencesWindow.cs |
| Label every note in the piano roll | Nommer chaque note dans le piano roll | PreferencesWindow.cs |
| Colour theme | Thème de couleurs | PreferencesWindow.cs |
| Themes change colours only. Follow system switches between your dark-mode and light-mode theme with the OS appearance. | Les thèmes ne changent que les couleurs. « Suivre le système » alterne entre le thème du mode sombre et celui du mode clair, selon l’apparence de l’OS. | PreferencesWindow.cs |
| Waveform | Forme d’onde | PreferencesWindow.cs |
| How audio clips draw on the arrangement: Smooth outlines the peaks around the RMS level; Solid draws every pixel; Bars spaces the peaks out so the rhythm reads. Solid and Bars also set the browser's preview. | Dessin des clips audio dans l’arrangement : « Lisse » trace le contour des pics autour du niveau RMS ; « Plein » dessine chaque pixel ; « Barres » espace les pics pour que le rythme se lise. « Plein » et « Barres » règlent aussi l’aperçu du navigateur. | PreferencesWindow.cs |
| Track headers | En-têtes de piste | PreferencesWindow.cs |
| Which side of the arrangement the track list sits on. Right starts the timeline at the left edge, next to the browser. Also in the View menu. | Côté de l’arrangement où se trouve la liste des pistes. « Droite » fait commencer la timeline au bord gauche, contre le navigateur. Aussi dans le menu Affichage. | PreferencesWindow.cs |
| Interface scale | Échelle de l’interface | PreferencesWindow.cs |
| Makes the whole interface larger or smaller. Timeline, piano roll and audio editor zoom work on top of it. | Agrandit ou réduit toute l’interface. Le zoom de la timeline, du piano roll et de l’éditeur audio s’applique par-dessus. | PreferencesWindow.cs |
| The language of menus, panels and messages. It changes when Nota restarts. Project files and the names you type are never translated. | La langue des menus, des panneaux et des messages. Elle change au redémarrage de Nota. Les fichiers de projet et les noms saisis ne sont jamais traduits. | PreferencesWindow.cs |
| Piano roll | Piano roll | PreferencesWindow.cs |
| Shows every note name (C, D, E…) on the piano roll's keys instead of only C. Helpful when learning notes. | Affiche le nom de chaque note (C, D, E…) sur les touches du piano roll, et non seulement celui des C. Utile pour apprendre les notes. | PreferencesWindow.cs |
| AI control (MCP) | Contrôle par IA (MCP) | PreferencesWindow.cs |
| New project / open a project | Nouveau projet / ouvrir un projet | PreferencesWindow.cs |
| Save (records a version) / save as | Enregistrer (crée une version) / enregistrer sous | PreferencesWindow.cs |
| Save a version with a note | Enregistrer une version avec une note | PreferencesWindow.cs |
| Quit Nota · asks about unsaved changes first | Quitter Nota · demande d’abord quoi faire des modifications non enregistrées | PreferencesWindow.cs |
| Enter / leave full screen — the title bar hides in full screen | Entrer dans le plein écran / en sortir : la barre de titre se masque en plein écran | PreferencesWindow.cs |
| The clip editor fills the window / back to the split it had | L’éditeur de clips remplit la fenêtre / retour à la répartition précédente | PreferencesWindow.cs |
| Double-click a panel edge | Double-clic sur le bord d’un panneau | PreferencesWindow.cs |
| Reset the detail panel, the Mixer or the browser to its default size | Rétablir la taille par défaut du panneau de détail, du mixeur ou du navigateur | PreferencesWindow.cs |
| Command palette | Palette de commandes | PreferencesWindow.cs |
| Open / close the command palette — from any window, even while typing | Ouvrir / fermer la palette de commandes : depuis n’importe quelle fenêtre, même pendant la saisie | PreferencesWindow.cs |
| Move through the results | Parcourir les résultats | PreferencesWindow.cs |
| Apply where the palette was opened (the target shows in the field) | Appliquer là où la palette a été ouverte (la cible s’affiche dans le champ) | PreferencesWindow.cs |
| An instrument or instrument preset on a new track · a track: open it in Devices | Un instrument ou un préréglage d’instrument sur une nouvelle piste · une piste : l’ouvrir dans Appareils | PreferencesWindow.cs |
| Replace the selected device instead of inserting after it | Remplacer l’appareil sélectionné au lieu d’insérer après lui | PreferencesWindow.cs |
| Apply and keep the palette open for the next device | Appliquer et garder la palette ouverte pour l’appareil suivant | PreferencesWindow.cs |
| Filter: all · actions · tracks · devices · presets | Filtre : tout · actions · pistes · appareils · préréglages | PreferencesWindow.cs |
| Type first to filter: actions, tracks, devices, presets, Modular nodes | Saisir d’abord pour filtrer : actions, pistes, appareils, préréglages, nœuds Modular | PreferencesWindow.cs |
| Close and go back where you were | Fermer et revenir où l’on était | PreferencesWindow.cs |
| Play / Stop · Stop goes back to where playback started | Lecture / Arrêt · l’arrêt revient là où la lecture a commencé | PreferencesWindow.cs |
| Play / pause · the playhead stays put and Play carries on from there | Lecture / pause · la tête de lecture reste en place et la lecture reprend à partir de là | PreferencesWindow.cs |
| Stop (again → back to the start) · in the Session grid it launches the selection | Arrêt (de nouveau → retour au début) · dans la grille Session, lance la sélection | PreferencesWindow.cs |
| Loop on / off · loop the selected clips or time range | Boucle activée / désactivée · mettre en boucle les clips ou la plage de temps sélectionnés | PreferencesWindow.cs |
| Double-click loop brace | Double-clic sur l’accolade de boucle | PreferencesWindow.cs |
| Drag loop edge · drag loop brace | Glisser le bord de la boucle · glisser l’accolade de boucle | PreferencesWindow.cs |
| Resize that end of the loop region · move the whole region · ⌥ off the grid | Redimensionner cette extrémité de la région de boucle · déplacer toute la région · ⌥ hors de la grille | PreferencesWindow.cs |
| Right-click ruler | Clic droit sur la règle | PreferencesWindow.cs |
| Show playhead · scroll the Arrangement to it without moving it · picks up a paused Follow | Afficher la tête de lecture · faire défiler l’arrangement jusqu’à elle sans la déplacer · reprend un suivi en pause | PreferencesWindow.cs |
| Loop recording: Overdub (every pass adds) / Replace (keep the latest pass) | Enregistrement en boucle : Superposer (chaque passage s’ajoute) / Remplacer (garder le dernier passage) | PreferencesWindow.cs |
| Click the position read-out | Clic sur l’affichage de la position | PreferencesWindow.cs |
| Switch it between bars and time · every ruler follows | Basculer entre mesures et temps · toutes les règles suivent | PreferencesWindow.cs |
| Arrangement & editing | Arrangement et édition | PreferencesWindow.cs |
| Undo / redo | Annuler / rétablir | PreferencesWindow.cs |
| Select all clips | Sélectionner tous les clips | PreferencesWindow.cs |
| Toggle automation mode | Activer / désactiver le mode automation | PreferencesWindow.cs |
| Toggle the Mixer view | Afficher / masquer la vue Mixeur | PreferencesWindow.cs |
| Cycle Devices / Pattern / Clip in the detail panel | Passer d’Appareils à Pattern puis à Clip dans le panneau de détail | PreferencesWindow.cs |
| Cancel the current gesture / clear the selection · close the patch-bay overlay | Annuler le geste en cours / effacer la sélection · fermer la baie de brassage | PreferencesWindow.cs |
| Group / ungroup selected tracks · on selected devices: group them into a rack / take a rack apart | Grouper / dégrouper les pistes sélectionnées · sur des appareils sélectionnés : les grouper dans un rack / défaire un rack | PreferencesWindow.cs |
| Copy / cut / paste clips, a time range or an automation range · pastes onto the track clicked last | Copier / couper / coller des clips, une plage de temps ou une plage d’automation · colle sur la dernière piste cliquée | PreferencesWindow.cs |
| Copy the selected clips to Session · the first scene row where they fit | Copier les clips sélectionnés vers Session · dans la première rangée de scènes où ils tiennent | PreferencesWindow.cs |
| Paste the last range as audio rendered through its devices | Coller la dernière plage sous forme d’audio rendu à travers ses appareils | PreferencesWindow.cs |
| Duplicate the selected clip(s) / range | Dupliquer le ou les clips / la plage sélectionnés | PreferencesWindow.cs |
| Duplicate time · insert a copy of the time range after it, pushing everything later right | Dupliquer le temps · insérer une copie de la plage de temps après elle, en repoussant tout ce qui suit | PreferencesWindow.cs |
| Insert silence · push everything from the range start right by its length | Insérer du silence · repousser tout ce qui suit le début de la plage de sa longueur | PreferencesWindow.cs |
| Split at the playhead · at the range edges | Découper à la tête de lecture · aux bords de la plage | PreferencesWindow.cs |
| Consolidate the selection into one clip per track | Consolider la sélection en un clip par piste | PreferencesWindow.cs |
| Deactivate / activate the selected clip(s) | Désactiver / activer le ou les clips sélectionnés | PreferencesWindow.cs |
| Nudge the selected clips by the grid step · stops at the song start | Décaler les clips sélectionnés du pas de la grille · s’arrête au début du morceau | PreferencesWindow.cs |
| Nudge the selected clips by a 64th note | Décaler les clips sélectionnés d’une quadruple croche | PreferencesWindow.cs |
| Move the selected clips to the next track that takes them, at the same beats | Déplacer les clips sélectionnés vers la piste suivante qui les accepte, sur les mêmes temps | PreferencesWindow.cs |
| Drag below the last track | Glisser sous la dernière piste | PreferencesWindow.cs |
| Move the clips to a new track like their own · one per source track | Déplacer les clips vers une nouvelle piste du même type que la leur · une par piste d’origine | PreferencesWindow.cs |
| Delete the selected clip / range | Supprimer le clip / la plage sélectionnés | PreferencesWindow.cs |
| Grid (Arrangement or clip editor — whichever you used last) | Grille (arrangement ou éditeur de clips : le dernier utilisé) | PreferencesWindow.cs |
| Narrow / widen the grid · in Auto, change the density | Réduire / élargir la grille · en Auto, changer la densité | PreferencesWindow.cs |
| Triplet grid on / off | Grille en triolets activée / désactivée | PreferencesWindow.cs |
| Snap to grid on / off | Magnétisme sur la grille activé / désactivé | PreferencesWindow.cs |
| Switch between the adaptive (Auto) and a fixed grid | Basculer entre la grille adaptative (Auto) et une grille fixe | PreferencesWindow.cs |
| Move freely, ignoring the grid | Déplacer librement, sans tenir compte de la grille | PreferencesWindow.cs |
| Audio clip editor on ADSR or an unwarped clip | Éditeur de clips audio sur ADSR ou sur un clip non warpé | PreferencesWindow.cs |
| Tracks (after a click in the track headers) | Pistes (après un clic dans les en-têtes de piste) | PreferencesWindow.cs |
| Select all tracks | Sélectionner toutes les pistes | PreferencesWindow.cs |
| Select a range of tracks / add or remove one track | Sélectionner une série de pistes / ajouter ou retirer une piste | PreferencesWindow.cs |
| Copy / cut / paste the selected tracks · paste lands after the last one | Copier / couper / coller les pistes sélectionnées · le collage se place après la dernière | PreferencesWindow.cs |
| Duplicate the selected tracks | Dupliquer les pistes sélectionnées | PreferencesWindow.cs |
| Delete the selected tracks · a group goes with its tracks | Supprimer les pistes sélectionnées · un groupe part avec ses pistes | PreferencesWindow.cs |
| Devices (after a click on a device) | Appareils (après un clic sur un appareil) | PreferencesWindow.cs |
| Select a range of devices (MIDI effects, instrument, audio effects) / add or remove one | Sélectionner une série d’appareils (effets MIDI, instrument, effets audio) / en ajouter ou en retirer un | PreferencesWindow.cs |
| Clear the device selection · so does a click on the chain's empty space | Effacer la sélection d’appareils · un clic dans l’espace vide de la chaîne fait de même | PreferencesWindow.cs |
| Copy / cut / paste the selected devices with all their settings · each lands in its section after the selected device · a copied instrument replaces the instrument it is pasted on | Copier / couper / coller les appareils sélectionnés avec tous leurs réglages · chacun se place dans sa section après l’appareil sélectionné · un instrument copié remplace l’instrument sur lequel il est collé | PreferencesWindow.cs |
| Duplicate the selected devices · each run right after itself | Dupliquer les appareils sélectionnés · chaque série juste après elle-même | PreferencesWindow.cs |
| Group the selected devices into a rack · audio effects: Audio Effect Rack · MIDI effects: MIDI Effect Rack · with the instrument: Instrument Rack | Grouper les appareils sélectionnés dans un rack · effets audio : Audio Effect Rack · effets MIDI : MIDI Effect Rack · avec l’instrument : Instrument Rack | PreferencesWindow.cs |
| Take the devices out of a selected one-chain rack | Sortir les appareils d’un rack à une seule chaîne sélectionné | PreferencesWindow.cs |
| Delete the selected devices · with the instrument, its track | Supprimer les appareils sélectionnés · avec l’instrument, sa piste | PreferencesWindow.cs |
| Drag a selected device's header | Glisser l’en-tête d’un appareil sélectionné | PreferencesWindow.cs |
| Move the selected devices of that section together | Déplacer ensemble les appareils sélectionnés de cette section | PreferencesWindow.cs |
| Drag a device onto a track | Glisser un appareil sur une piste | PreferencesWindow.cs |
| Move it there (out of the panel, onto a track row or Session column) · the instrument replaces that track's and its own track goes | L’y déplacer (hors du panneau, sur une rangée de piste ou une colonne Session) · l’instrument remplace celui de cette piste et sa propre piste disparaît | PreferencesWindow.cs |
| Copy it there | L’y copier | PreferencesWindow.cs |
| Racks | Racks | PreferencesWindow.cs |
| End macro mapping (Map on the rack card) | Terminer l’assignation de macro (Assigner sur la carte du rack) | PreferencesWindow.cs |
| Drag a zone edge | Glisser le bord d’une zone | PreferencesWindow.cs |
| Set a chain's key, velocity or select range · edges never cross | Régler la plage de notes, de vélocité ou de sélection d’une chaîne · les bords ne se croisent jamais | PreferencesWindow.cs |
| Hover the zones | Survoler les zones | PreferencesWindow.cs |
| See which chains a note would play | Voir quelles chaînes jouerait une note | PreferencesWindow.cs |
| Double-click a chain or macro name | Double-clic sur le nom d’une chaîne ou d’une macro | PreferencesWindow.cs |
| Rename it | La renommer | PreferencesWindow.cs |
| Map it to a parameter or the selector · clear its mappings | L’assigner à un paramètre ou au sélecteur · effacer ses assignations | PreferencesWindow.cs |
| Right-click a macro | Clic droit sur une macro | PreferencesWindow.cs |
| Drag a device's header | Glisser l’en-tête d’un appareil | PreferencesWindow.cs |
| Move it within its chain · onto another chain's row to move it there | Le déplacer dans sa chaîne · sur la rangée d’une autre chaîne pour l’y déplacer | PreferencesWindow.cs |
| Click a device | Clic sur un appareil | PreferencesWindow.cs |
| Select it for ⌘C ⌘X ⌘V ⌘D and Delete | Le sélectionner pour ⌘C ⌘X ⌘V ⌘D et Delete | PreferencesWindow.cs |
| Modular (after a click on the canvas) | Modular (après un clic sur le canevas) | PreferencesWindow.cs |
| Fit every node on screen | Ajuster tous les nœuds à l’écran | PreferencesWindow.cs |
| Bypass the selected device | Contourner l’appareil sélectionné | PreferencesWindow.cs |
| Duplicate the selected device with all its settings | Dupliquer l’appareil sélectionné avec tous ses réglages | PreferencesWindow.cs |
| Delete the selected device, modulator or cable · the track's input, instrument and output are fixed | Supprimer l’appareil, le modulateur ou le câble sélectionné · l’entrée, l’instrument et la sortie de la piste sont fixes | PreferencesWindow.cs |
| Show fewer parameters on every opened node | Afficher moins de paramètres sur chaque nœud ouvert | PreferencesWindow.cs |
| Session (after a click in the grid) | Session (après un clic dans la grille) | PreferencesWindow.cs |
| Move the selection between slots and scenes | Déplacer la sélection entre les emplacements et les scènes | PreferencesWindow.cs |
| Launch the selected clip or scene | Lancer le clip ou la scène sélectionnés | PreferencesWindow.cs |
| Copy / cut / paste a clip · a cut clip moves when pasted · clips copied in the Arrangement paste here | Copier / couper / coller un clip · un clip coupé se déplace au collage · les clips copiés dans l’arrangement se collent ici | PreferencesWindow.cs |
| Copy the selected clip, group slot or scene to the Arrangement at the playhead | Copier le clip, l’emplacement de groupe ou la scène sélectionnés vers l’arrangement, à la tête de lecture | PreferencesWindow.cs |
| Duplicate a clip into the slot below · a scene below itself | Dupliquer un clip dans l’emplacement du dessous · une scène sous elle-même | PreferencesWindow.cs |
| Insert a scene below the selection | Insérer une scène sous la sélection | PreferencesWindow.cs |
| Rename the selected clip or scene | Renommer le clip ou la scène sélectionnés | PreferencesWindow.cs |
| Delete the selected clip or scene | Supprimer le clip ou la scène sélectionnés | PreferencesWindow.cs |
| Drag clip | Glisser un clip | PreferencesWindow.cs |
| Move it to another slot · ⌥ copies | Le déplacer vers un autre emplacement · ⌥ copie | PreferencesWindow.cs |
| Drag scene | Glisser une scène | PreferencesWindow.cs |
| Reorder scenes | Réordonner les scènes | PreferencesWindow.cs |
| Double-click slot | Double-clic sur un emplacement | PreferencesWindow.cs |
| Edit the clip · an empty MIDI slot gets a new clip | Éditer le clip · un emplacement MIDI vide reçoit un nouveau clip | PreferencesWindow.cs |
| Double-click header | Double-clic sur un en-tête | PreferencesWindow.cs |
| Rename the track | Renommer la piste | PreferencesWindow.cs |
| Add an instrument, audio or return track · rename or delete one | Ajouter une piste d’instrument, audio ou de retour · en renommer ou en supprimer une | PreferencesWindow.cs |
| Right-click header or grid | Clic droit sur un en-tête ou la grille | PreferencesWindow.cs |
| Move notes by the grid | Déplacer les notes selon la grille | PreferencesWindow.cs |
| Transpose by a semitone | Transposer d’un demi-ton | PreferencesWindow.cs |
| Transpose by an octave | Transposer d’une octave | PreferencesWindow.cs |
| Shorten / lengthen the notes by the grid | Raccourcir / allonger les notes selon la grille | PreferencesWindow.cs |
| Velocity +10 / −10 · ⌥⇧ by 1 | Vélocité +10 / −10 · ⌥⇧ par 1 | PreferencesWindow.cs |
| Change its velocity · the whole selection's when the note is selected · elsewhere scroll sideways | Changer sa vélocité · celle de toute la sélection si la note est sélectionnée · ailleurs, défiler latéralement | PreferencesWindow.cs |
| Select all notes | Sélectionner toutes les notes | PreferencesWindow.cs |
| Duplicate the selection · replaces the notes it lands on | Dupliquer la sélection · remplace les notes sur lesquelles elle tombe | PreferencesWindow.cs |
| Copy / cut / paste notes · paste lands on the originals | Copier / couper / coller des notes · le collage se place sur les originaux | PreferencesWindow.cs |
| Deselect · moved or pasted notes replace the notes under them | Désélectionner · les notes déplacées ou collées remplacent celles qui sont dessous | PreferencesWindow.cs |
| Delete the selected notes | Supprimer les notes sélectionnées | PreferencesWindow.cs |
| Add a note with the length and velocity of the last note touched · on a note, delete it | Ajouter une note avec la longueur et la vélocité de la dernière note touchée · sur une note, la supprimer | PreferencesWindow.cs |
| Double-click empty grid | Double-clic sur la grille vide | PreferencesWindow.cs |
| Double-click and hold, drag | Double-clic maintenu, puis glisser | PreferencesWindow.cs |
| Change the note's start or end | Changer le début ou la fin de la note | PreferencesWindow.cs |
| Drag note edge | Glisser le bord d’une note | PreferencesWindow.cs |
| Click · drag the ruler | Clic · glisser sur la règle | PreferencesWindow.cs |
| Move the playhead there · drag to scrub, snapped to the grid · ⌥ off the grid | Y déplacer la tête de lecture · glisser pour balayer, calé sur la grille · ⌥ hors de la grille | PreferencesWindow.cs |
| Drag the playhead line | Glisser la ligne de la tête de lecture | PreferencesWindow.cs |
| Scrub the playhead · on the ruler or over empty grid | Balayer avec la tête de lecture · sur la règle ou sur la grille vide | PreferencesWindow.cs |
| Drag velocity stem | Glisser une tige de vélocité | PreferencesWindow.cs |
| Set velocity · selected notes move together · ⇧ adds a note | Régler la vélocité · les notes sélectionnées bougent ensemble · ⇧ ajoute une note | PreferencesWindow.cs |
| Drag in the Bend / Pres / Slide lane | Glisser dans le couloir Bend / Pres / Slide | PreferencesWindow.cs |
| Draw the selected note's MPE curve · ⌥ draws a straight line · drag a point to move it | Dessiner la courbe MPE de la note sélectionnée · ⌥ trace une ligne droite · glisser un point pour le déplacer | PreferencesWindow.cs |
| Add a point · on a point, delete it · right-click to clear the curve | Ajouter un point · sur un point, le supprimer · clic droit pour effacer la courbe | PreferencesWindow.cs |
| Double-click in an MPE lane | Double-clic dans un couloir MPE | PreferencesWindow.cs |
| Click the controller tab | Clic sur l’onglet de contrôleur | PreferencesWindow.cs |
| Show a MIDI controller under the notes · click it again to pick another CC, the pitch wheel or channel pressure | Afficher un contrôleur MIDI sous les notes · cliquer à nouveau pour choisir un autre CC, la molette de hauteur ou la pression de canal | PreferencesWindow.cs |
| Drag in the Sustain lane | Glisser dans le couloir Sustain | PreferencesWindow.cs |
| Draw a pedal block on the grid · ⌘ off the grid · drag an edge to resize, the body to move · double-click or Delete removes it | Dessiner un bloc de pédale sur la grille · ⌘ hors de la grille · glisser un bord pour redimensionner, le corps pour déplacer · double-clic ou Delete le supprime | PreferencesWindow.cs |
| Drag in a controller lane | Glisser dans un couloir de contrôleur | PreferencesWindow.cs |
| Draw the controller's curve · ⌥ draws a straight line · drag a point to move it · double-click adds or deletes a point | Dessiner la courbe du contrôleur · ⌥ trace une ligne droite · glisser un point pour le déplacer · double-clic ajoute ou supprime un point | PreferencesWindow.cs |
| Click a clip in the clip strip | Clic sur un clip du bandeau de clips | PreferencesWindow.cs |
| Make it the active clip (several clips open) · its lock keeps it as a read-only reference | En faire le clip actif (plusieurs clips ouverts) · son verrou le garde comme référence en lecture seule | PreferencesWindow.cs |
| Zoom rows (vertical zoom) | Zoom des rangées (zoom vertical) | PreferencesWindow.cs |
| Click a key | Clic sur une touche | PreferencesWindow.cs |
| Play it on the track's instrument · ⇧-drag glides through the keys · ⌘-click selects every note of that pitch | La jouer sur l’instrument de la piste · ⇧-glisser parcourt les touches · ⌘-clic sélectionne toutes les notes de cette hauteur | PreferencesWindow.cs |
| Drag on the keys | Glisser sur les touches | PreferencesWindow.cs |
| Zoom rows · down taller, up shorter | Zoom des rangées · vers le bas plus hautes, vers le haut plus basses | PreferencesWindow.cs |
| Zoom rows (trackpad) | Zoom des rangées (trackpad) | PreferencesWindow.cs |
| Fit to notes | Ajuster aux notes | PreferencesWindow.cs |
| Fold: only the scale's notes, or a Drum Rack's pads | Replier : seulement les notes de la gamme, ou les pads d’un Drum Rack | PreferencesWindow.cs |
| Audio clip editor | Éditeur de clips audio | PreferencesWindow.cs |
| Move the playhead there · drag to scrub · snapped to the grid on a warped clip, ⌥ off it | Y déplacer la tête de lecture · glisser pour balayer · calé sur la grille sur un clip warpé, ⌥ hors de la grille | PreferencesWindow.cs |
| Scrub the playhead | Balayer avec la tête de lecture | PreferencesWindow.cs |
| Add a warp marker · on a marker, delete it | Ajouter un marqueur de warp · sur un marqueur, le supprimer | PreferencesWindow.cs |
| Double-click the wave | Double-clic sur la forme d’onde | PreferencesWindow.cs |
| Align the audio to the grid · the wave stretches as you drag | Aligner l’audio sur la grille · la forme d’onde s’étire pendant le glissement | PreferencesWindow.cs |
| Drag a warp marker | Glisser un marqueur de warp | PreferencesWindow.cs |
| While dragging a warp marker or a segment tempo: put it back | Pendant le glissement d’un marqueur de warp ou d’un tempo de segment : le remettre en place | PreferencesWindow.cs |
| Drag · double-click Tempo | Glisser · double-clic sur Tempo | PreferencesWindow.cs |
| Play notes (computer keyboard) | Jouer des notes (clavier de l’ordinateur) | PreferencesWindow.cs |
| White keys, from C4 | Touches blanches, à partir de C4 | PreferencesWindow.cs |
| Black keys (sharps) | Touches noires (dièses) | PreferencesWindow.cs |
| Shift octave down / up | Octave vers le bas / vers le haut | PreferencesWindow.cs |
| Lower / raise velocity | Baisser / augmenter la vélocité | PreferencesWindow.cs |
| Browser · preview | Navigateur · aperçu | PreferencesWindow.cs |
| Move through samples, presets and devices · with Auto on, each one plays as it is selected | Parcourir les échantillons, les préréglages et les appareils · avec Auto activé, chacun est joué dès qu’il est sélectionné | PreferencesWindow.cs |
| Click play button | Clic sur le bouton de lecture | PreferencesWindow.cs |
| Click waveform | Clic sur la forme d’onde | PreferencesWindow.cs |
| Play from that point | Lire à partir de ce point | PreferencesWindow.cs |
| Click WAVE / SPEC | Clic sur WAVE / SPEC | PreferencesWindow.cs |
| Switch the well between the waveform and a live spectrum | Basculer la zone entre la forme d’onde et un spectre en direct | PreferencesWindow.cs |
| Scroll on the track button | Défiler sur le bouton de piste | PreferencesWindow.cs |
| Step through the demo tracks an effect preset is heard through | Parcourir les pistes de démonstration à travers lesquelles un préréglage d’effet s’écoute | PreferencesWindow.cs |
| Type the preview volume · double-click resets it to 0 dB | Saisir le volume d’aperçu · double-clic le remet à 0 dB | PreferencesWindow.cs |
| Gamepad (macOS) | Manette (macOS) | PreferencesWindow.cs |
| Play C4 D4 E4 F4 | Jouer C4 D4 E4 F4 | PreferencesWindow.cs |
| Play G4 A4 B4 C5 | Jouer G4 A4 B4 C5 | PreferencesWindow.cs |
| Shift the gamepad octave | Changer l’octave de la manette | PreferencesWindow.cs |
| Any button | N’importe quel bouton | PreferencesWindow.cs |
| Mappable through MIDI Learn — a mapped button drives the control instead | Assignable par MIDI Learn : un bouton assigné pilote la commande à la place | PreferencesWindow.cs |
| Mappable as continuous controls (a knob, a fader); they play no notes | Assignables comme commandes continues (un bouton rotatif, un fader) ; ils ne jouent aucune note | PreferencesWindow.cs |
| Sticks · triggers | Sticks · gâchettes | PreferencesWindow.cs |
| Mouse | Souris | PreferencesWindow.cs |
| Double-click clip | Double-clic sur un clip | PreferencesWindow.cs |
| Open in the clip editor · on one of several selected MIDI clips, open them all together | Ouvrir dans l’éditeur de clips · sur l’un de plusieurs clips MIDI sélectionnés, les ouvrir tous ensemble | PreferencesWindow.cs |
| Double-click a browser instrument | Double-clic sur un instrument du navigateur | PreferencesWindow.cs |
| Swap it onto the selected instrument track · clips and effects stay · no track selected starts one | L’échanger sur la piste d’instrument sélectionnée · les clips et les effets restent · sans piste sélectionnée, en crée une | PreferencesWindow.cs |
| Always start a new track with it | Toujours créer une nouvelle piste avec lui | PreferencesWindow.cs |
| Right-click the metronome | Clic droit sur le métronome | PreferencesWindow.cs |
| Drag an audio clip's ADSR handle | Glisser la poignée ADSR d’un clip audio | PreferencesWindow.cs |
| Shape attack · decay + sustain · release (top corners and the dot on hover) · ⌥-click types the stage · double-click resets it | Façonner l’attaque · le déclin + le maintien · le relâchement (coins du haut et point au survol) · ⌥-clic permet de saisir l’étape · double-clic la réinitialise | PreferencesWindow.cs |
| Add the track to the selection · again to remove it | Ajouter la piste à la sélection · de nouveau pour la retirer | PreferencesWindow.cs |
| Right-click track headers | Clic droit sur les en-têtes de piste | PreferencesWindow.cs |
| Drag a track's bottom edge | Glisser le bord inférieur d’une piste | PreferencesWindow.cs |
| Drag a track's bottom edge + ⌥ | Glisser le bord inférieur d’une piste + ⌥ | PreferencesWindow.cs |
| Resize every track at once | Redimensionner toutes les pistes à la fois | PreferencesWindow.cs |
| Back to the standard height | Retour à la hauteur standard | PreferencesWindow.cs |
| Double-click a track's bottom edge | Double-clic sur le bord inférieur d’une piste | PreferencesWindow.cs |
| Drag a return's or the master's bottom edge | Glisser le bord inférieur d’un retour ou du master | PreferencesWindow.cs |
| Resize that row (header or lane) · same snaps · double-click goes back to compact | Redimensionner cette rangée (en-tête ou couloir) · mêmes crans · double-clic revient à compact | PreferencesWindow.cs |
| Select several returns · they resize together | Sélectionner plusieurs retours · ils se redimensionnent ensemble | PreferencesWindow.cs |
| Set every track to Compact · Standard · Large | Mettre toutes les pistes en Compact · Standard · Grand | PreferencesWindow.cs |
| Drag the track-list edge | Glisser le bord de la liste des pistes | PreferencesWindow.cs |
| Widen or narrow the track headers · double-click resets | Élargir ou rétrécir les en-têtes de piste · double-clic réinitialise | PreferencesWindow.cs |
| Drag sections lane | Glisser dans le couloir des sections | PreferencesWindow.cs |
| Mark a new section over the dragged bars | Marquer une nouvelle section sur les mesures parcourues | PreferencesWindow.cs |
| Click section | Clic sur une section | PreferencesWindow.cs |
| Jump the playhead to its start | Placer la tête de lecture à son début | PreferencesWindow.cs |
| Double-click section | Double-clic sur une section | PreferencesWindow.cs |
| Rename it · right-click → Reset to default name for its automatic Section N | La renommer · clic droit → « Rétablir le nom par défaut » pour sa « Section N » automatique | PreferencesWindow.cs |
| Return · Esc (renaming a section) | Return · Esc (renommage d’une section) | PreferencesWindow.cs |
| Drag section · its edge | Glisser une section · son bord | PreferencesWindow.cs |
| Drag overview strip | Glisser le bandeau d’aperçu | PreferencesWindow.cs |
| Drag overview edge | Glisser le bord de l’aperçu | PreferencesWindow.cs |
| Zoom by resizing the viewport window | Zoomer en redimensionnant la fenêtre de visualisation | PreferencesWindow.cs |
| Double-click overview | Double-clic sur l’aperçu | PreferencesWindow.cs |
| Fit the whole project on screen | Ajuster tout le projet à l’écran | PreferencesWindow.cs |
| Drag a device header | Glisser l’en-tête d’un appareil | PreferencesWindow.cs |
| Right-click a device header | Clic droit sur l’en-tête d’un appareil | PreferencesWindow.cs |
| Delete (device selected) | Delete (appareil sélectionné) | PreferencesWindow.cs |
| Remove the selected device — the header no longer carries a close button | Supprimer l’appareil sélectionné : l’en-tête n’a plus de bouton de fermeture | PreferencesWindow.cs |
| Drag an audio clip onto an instrument track | Glisser un clip audio sur une piste d’instrument | PreferencesWindow.cs |
| Load its audio into the Sampler, Grain, Drum Rack (next free pad) or Instrument Rack · rest there to open its devices | Charger son audio dans le Sampler, le Grain, le Drum Rack (prochain pad libre) ou l’Instrument Rack · s’y arrêter pour ouvrir ses appareils | PreferencesWindow.cs |
| Drag an audio clip out of the Arrangement | Glisser un clip audio hors de l’arrangement | PreferencesWindow.cs |
| Take it as a sample: drop on a pad, a Grain, a Rhythm voice, a Session slot — the clip stays put | Le prendre comme échantillon : le déposer sur un pad, un Grain, une voix Rhythm, un emplacement Session ; le clip reste en place | PreferencesWindow.cs |
| Drag clip + ⌥ | Glisser un clip + ⌥ | PreferencesWindow.cs |
| Position freely, ignoring the grid for this drag (the magnet in the transport latches the same thing) | Positionner librement, sans tenir compte de la grille pour ce glissement (l’aimant du transport verrouille la même chose) | PreferencesWindow.cs |
| Scroll it sideways · over an EQ band's handle the wheel sets its Q, over a value box it steps the value | Le faire défiler latéralement · sur la poignée d’une bande d’EQ, la molette règle son Q ; sur une case de valeur, elle change la valeur par pas | PreferencesWindow.cs |
| Wheel over the device chain | Molette sur la chaîne d’appareils | PreferencesWindow.cs |
| Change a device value · hold ⌘ or ⇧ for fine steps | Changer une valeur d’appareil · maintenir ⌘ ou ⇧ pour des pas fins | PreferencesWindow.cs |
| Drag knob | Glisser un bouton rotatif | PreferencesWindow.cs |
| Drag slider | Glisser un curseur | PreferencesWindow.cs |
| Type a value (units like k, ms, dB, L / R work) · double-click its value does the same | Saisir une valeur (les unités comme k, ms, dB, L / R fonctionnent) · un double-clic sur sa valeur fait de même | PreferencesWindow.cs |
| Return · Esc · Tab / ⇧Tab (typing a value) | Return · Esc · Tab / ⇧Tab (saisie d’une valeur) | PreferencesWindow.cs |
| Set it · cancel · set it and type the next / previous value on the card | La valider · annuler · la valider et saisir la valeur suivante / précédente de la carte | PreferencesWindow.cs |
| Type its values — a node with several (frequency · gain · Q, decay · sustain) takes Tab between them | Saisir ses valeurs : un nœud qui en a plusieurs (fréquence · gain · Q, déclin · maintien) utilise Tab pour passer de l’une à l’autre | PreferencesWindow.cs |
| Double-click a knob, fader or slider | Double-clic sur un bouton rotatif, un fader ou un curseur | PreferencesWindow.cs |
| Reset the value to its default | Remettre la valeur par défaut | PreferencesWindow.cs |
| Clear the route (Volt, Aurora) · pull the cable (Consort) | Effacer la route (Volt, Aurora) · retirer le câble (Consort) | PreferencesWindow.cs |
| Double-click a mod-matrix cell | Double-clic sur une cellule de la matrice de modulation | PreferencesWindow.cs |
| Right-click a knob, fader or slider | Clic droit sur un bouton rotatif, un fader ou un curseur | PreferencesWindow.cs |
| Type value… · Reset to default | Saisir une valeur… · Rétablir la valeur par défaut | PreferencesWindow.cs |
| Drag jack → jack | Glisser jack → jack | PreferencesWindow.cs |
| Patch a cable in Nota Consort · ⌥-click a jack to pull its cables | Brancher un câble dans Nota Consort · ⌥-clic sur un jack pour retirer ses câbles | PreferencesWindow.cs |
| Click · ⇧ · ⌥ a Rhythm step | Clic · ⇧ · ⌥ sur un pas Rhythm | PreferencesWindow.cs |
| Click a Rhythm voice | Clic sur une voix Rhythm | PreferencesWindow.cs |
| Select and play it · drop a file on it to load a sample | La sélectionner et la jouer · y déposer un fichier pour charger un échantillon | PreferencesWindow.cs |
| Filter shortcuts | Filtrer les raccourcis | PreferencesWindow.cs |
| Wheel up lowers the velocity | Molette vers le haut : baisse la vélocité | PreferencesWindow.cs |
| Velocity wheel | Molette de vélocité | PreferencesWindow.cs |
| Direction | Sens | PreferencesWindow.cs |
| Step | Pas | PreferencesWindow.cs |
| System default | Par défaut du système | PreferencesWindow.cs |
| Channels 2–16 each carry one note with its own pitch bend, pressure and slide (CC74). A controller that announces its range overrides this one. Channel 1 plays as an ordinary keyboard, its pitch wheel and aftertouch moving the whole instrument.  | Les canaux 2 à 16 portent chacun une note avec son propre pitch bend, sa pression et son glissement (CC74). Un contrôleur qui annonce son amplitude remplace celle-ci. Le canal 1 se joue comme un clavier ordinaire, sa molette de hauteur et son aftertouch agissant sur tout l’instrument.  | PreferencesWindow.cs |
| Nota Synth, Volt, Aurora, Operator, Pentad and Physical play per-note expression. | Nota Synth, Volt, Aurora, Operator, Pentad et Physical jouent l’expression par note. | PreferencesWindow.cs |
| ⌥-drag a device onto a track | ⌥-glisser un appareil sur une piste | PreferencesWindow.cs |
| ⇧ + wheel over a note | ⇧ + molette sur une note | PreferencesWindow.cs |
| ⇧-click track header | ⇧-clic sur un en-tête de piste | PreferencesWindow.cs |
| ⌥-click a height key in the track menu | ⌥-clic sur une touche de hauteur dans le menu de piste | PreferencesWindow.cs |
| ⌥-click a knob, fader or slider | ⌥-clic sur un bouton rotatif, un fader ou un curseur | PreferencesWindow.cs |
| ⌥-click a graph handle, envelope node or mod-matrix cell | ⌥-clic sur une poignée de graphique, un nœud d’enveloppe ou une cellule de la matrice de modulation | PreferencesWindow.cs |
| ⇧ + wheel over a note in the piano roll changes its velocity, or the whole selection's when that note is selected: one MIDI step per notch at 1×. Anywhere else ⇧ + wheel scrolls, and Off leaves it scrolling everywhere. | ⇧ + molette sur une note du piano roll change sa vélocité, ou celle de toute la sélection si cette note est sélectionnée : un pas MIDI par cran à 1×. Partout ailleurs, ⇧ + molette fait défiler, et « Désactivé » la laisse défiler partout. | PreferencesWindow.cs |
| Devices\|settings | Périphériques | PreferencesWindow.cs |
| Loop on / off in the ruler · the region stays, greyed out while off | Boucle activée / désactivée dans la règle · la région reste, grisée tant qu’elle est désactivée | PreferencesWindow.cs |
| Loop on / off · loop the selection · clear the loop region | Boucle activée / désactivée · mettre la sélection en boucle · effacer la région de boucle | PreferencesWindow.cs |
| Shape the new note: move it · ⇧ set its length · ⌘ drag up / down for velocity | Façonner la nouvelle note : la déplacer · ⇧ pour régler sa longueur · ⌘ glisser vers le haut / le bas pour la vélocité | PreferencesWindow.cs |
| Set the tempo the clip was recorded at · ÷2 / ×2 halve or double it | Régler le tempo auquel le clip a été enregistré · ÷2 / ×2 le divisent ou le doublent | PreferencesWindow.cs |
| Play / stop the selected sample, preset or device | Lire / arrêter l’échantillon, le préréglage ou l’appareil sélectionné | PreferencesWindow.cs |
| Count-in before Play / Record: off · 1 · 2 · 4 bars | Décompte avant Lecture / Enregistrement : désactivé · 1 · 2 · 4 mesures | PreferencesWindow.cs |
| A multi-selection gets its own menu: colour, height, freeze, group, copy, duplicate, delete | Une sélection multiple a son propre menu : couleur, hauteur, gel, groupe, copie, duplication, suppression | PreferencesWindow.cs |
| Resize the track (header or lane) · snaps to compact / standard / large · a multi-selection resizes together | Redimensionner la piste (en-tête ou couloir) · se cale sur compact / standard / grand · une sélection multiple se redimensionne ensemble | PreferencesWindow.cs |
| Move / resize it, snapped to bars | La déplacer / redimensionner, calée sur les mesures | PreferencesWindow.cs |
| Drag sideways to scroll · up / down to zoom out / in | Glisser latéralement pour défiler · vers le haut / le bas pour dézoomer / zoomer | PreferencesWindow.cs |
| Reorder devices (or right-click the header → Move left / right) | Réordonner les appareils (ou clic droit sur l’en-tête → Déplacer à gauche / à droite) | PreferencesWindow.cs |
| A / B compare, move, copy, delete, presets, save preset | Comparaison A / B, déplacer, copier, supprimer, préréglages, enregistrer le préréglage | PreferencesWindow.cs |
| Left / right (or up / down) changes the value · hold ⌘ or ⇧ for fine steps | Gauche / droite (ou haut / bas) change la valeur · maintenir ⌘ ou ⇧ pour des pas fins | PreferencesWindow.cs |
| Step on / off · accent · quiet step — drag across to paint, up / down (or scroll) for its velocity | Pas activé / désactivé · accent · pas doux — glisser pour peindre, haut / bas (ou défilement) pour sa vélocité | PreferencesWindow.cs |
| D-pad up / down | Croix ↑ / ↓ | PreferencesWindow.cs |
| D-pad left / right | Croix ← / → | PreferencesWindow.cs |
| Keep the name (empty = automatic) · cancel | Garder le nom (vide = automatique) · annuler | PreferencesWindow.cs |

## Accueil, À propos, Nouveautés (25)

| English | Français | Source |
|---|---|---|
| Nota | Nota | AboutWindow.cs |
| Version {0} | Version {0} | AboutWindow.cs |
| Engine {0} | Moteur {0} | AboutWindow.cs |
| Desktop DAW · AGPLv3 | DAW de bureau · AGPLv3 | AboutWindow.cs |
| © 2026 Egor Khindikaynen (Ambertape) | © 2026 Egor Khindikaynen (Ambertape) | AboutWindow.cs |
| Free software under the GNU AGPLv3. | Logiciel libre sous licence GNU AGPLv3. | AboutWindow.cs |
| Includes third-party software (JUCE, Avalonia, miniaudio, RtMidi, dr_libs, Signalsmith, HIIR). See LICENSES/THIRD-PARTY-NOTICES.md for copyright and attribution notices. | Inclut des logiciels tiers (JUCE, Avalonia, miniaudio, RtMidi, dr_libs, Signalsmith, HIIR). Voir LICENSES/THIRD-PARTY-NOTICES.md pour les mentions de droits d’auteur et d’attribution. | AboutWindow.cs |
| Settings | Réglages | WelcomeWindow.cs |
| Recover | Récupérer | WelcomeWindow.cs |
| Welcome to Nota | Bienvenue dans Nota | WelcomeWindow.cs |
| Version {0} · Engine {1} · Desktop DAW | Version {0} · Moteur {1} · DAW de bureau | WelcomeWindow.cs |
| Open project… | Ouvrir un projet… | WelcomeWindow.cs |
| Recent projects | Projets récents | WelcomeWindow.cs |
| No recent projects | Aucun projet récent | WelcomeWindow.cs |
| Show on startup | Afficher au démarrage | WelcomeWindow.cs |
| What's New | Nouveautés | WelcomeWindow.cs |
| Nota {0} is ready to install | Nota {0} est prêt à être installé | WelcomeWindow.cs |
| Restart Nota to finish updating — or it installs the next time you quit. | Redémarrer Nota pour terminer la mise à jour — ou elle s’installe à la prochaine fermeture. | WelcomeWindow.cs |
| A new version of Nota is available | Une nouvelle version de Nota est disponible | WelcomeWindow.cs |
| Unsaved work available | Travail non enregistré disponible | WelcomeWindow.cs |
| Opening… | Ouverture… | WelcomeWindow.cs |
| What's new in Nota | Nouveautés de Nota | WhatsNewWindow.cs |
| No release notes available. | Aucune note de version disponible. | WhatsNewWindow.cs |
| Got it | Compris | WhatsNewWindow.cs |
| All changes | Toutes les modifications | WhatsNewWindow.cs |

## Palette de commandes (92)

| English | Français | Source |
|---|---|---|
| Undo | Annuler | CommandPaletteView.cs |
| Search actions, tracks, devices, presets… | Rechercher des actions, pistes, appareils, préréglages… | CommandPaletteView.cs |
| 1 result | 1 résultat | CommandPaletteView.cs |
| {0} results | {0} résultats | CommandPaletteView.cs |
| Showing results for  | Résultats pour  | CommandPaletteView.cs |
|  · typed in the other keyboard layout |  · saisi dans l’autre disposition de clavier | CommandPaletteView.cs |
| Nothing matches. Try a role (reverb, comp), a character (warm, dark) or a source (vocals, drums). | Aucun résultat. Essayer un rôle (reverb, comp), un caractère (warm, dark) ou une source (vocals, drums). | CommandPaletteView.cs |
| run | exécuter | CommandPaletteView.cs |
| open in Devices | ouvrir dans Appareils | CommandPaletteView.cs |
| select | sélectionner | CommandPaletteView.cs |
| add & stay | ajouter et rester | CommandPaletteView.cs |
| add to canvas | ajouter au canevas | CommandPaletteView.cs |
| load | charger | CommandPaletteView.cs |
| new track | nouvelle piste | CommandPaletteView.cs |
| insert | insérer | CommandPaletteView.cs |
| replace | remplacer | CommandPaletteView.cs |
| filter | filtrer | CommandPaletteView.cs |
| Not available | Indisponible | MainWindow.Palette.cs |
| Opened {0} in Devices | {0} ouvert dans Appareils | MainWindow.Palette.cs |
| Selected {0} | {0} sélectionné | MainWindow.Palette.cs |
| Couldn't add {0}. | Impossible d’ajouter {0}. | MainWindow.Palette.cs |
| {0} added to the {1} canvas — connect it to a parameter | {0} ajouté au canevas {1} — le relier à un paramètre | MainWindow.Palette.cs |
| Save project | Enregistrer le projet | CommandCatalog.cs |
| Save project as… | Enregistrer le projet sous… | CommandCatalog.cs |
| Exit Nota | Quitter Nota | CommandCatalog.cs |
| Activate / deactivate clip | Activer / désactiver le clip | CommandCatalog.cs |
| Show Arrangement | Afficher l’arrangement | CommandCatalog.cs |
| Show Session | Afficher la vue Session | CommandCatalog.cs |
| Show Modular | Afficher la vue Modular | CommandCatalog.cs |
| Show Mixer | Afficher le mixeur | CommandCatalog.cs |
| Show devices | Afficher les appareils | CommandCatalog.cs |
| Open devices in a window | Ouvrir les appareils dans une fenêtre | CommandCatalog.cs |
| Reset section name | Rétablir le nom de la section | CommandCatalog.cs |
| Cycle clip names | Faire défiler les noms de clips | CommandCatalog.cs |
| Show version history | Afficher l’historique des versions | CommandCatalog.cs |
| MIDI learn | Apprentissage MIDI | CommandCatalog.cs |
| Toggle loop | Activer / désactiver la boucle | CommandCatalog.cs |
| New MIDI track | Nouvelle piste MIDI | CommandCatalog.cs |
| New audio track | Nouvelle piste audio | CommandCatalog.cs |
| New return track | Nouvelle piste de retour | CommandCatalog.cs |
| Ungroup tracks | Dégrouper les pistes | CommandCatalog.cs |
| Quantize notes | Quantifier les notes | CommandCatalog.cs |
| Transpose notes up an octave | Transposer les notes d’une octave vers le haut | CommandCatalog.cs |
| Transpose notes down an octave | Transposer les notes d’une octave vers le bas | CommandCatalog.cs |
| Remove out-of-scale notes | Supprimer les notes hors gamme | CommandCatalog.cs |
| Fold the piano roll | Replier le piano roll | CommandCatalog.cs |
| Toggle clip editor track mode | Activer / désactiver le mode piste de l’éditeur de clips | CommandCatalog.cs |
| Toggle triplet grid | Activer / désactiver la grille en triolets | CommandCatalog.cs |
| Toggle snap to grid | Activer / désactiver le magnétisme sur la grille | CommandCatalog.cs |
| Toggle adaptive grid | Activer / désactiver la grille adaptative | CommandCatalog.cs |
| Settings: Audio | Réglages : Audio | CommandCatalog.cs |
| Settings: MIDI | Réglages : MIDI | CommandCatalog.cs |
| Settings: Gamepads | Réglages : Manettes | CommandCatalog.cs |
| Settings: Remote | Réglages : Télécommande | CommandCatalog.cs |
| Settings: Plug-ins | Réglages : Plug-ins | CommandCatalog.cs |
| Settings: Downloads | Réglages : Téléchargements | CommandCatalog.cs |
| Settings: Library | Réglages : Bibliothèque | CommandCatalog.cs |
| Settings: Appearance | Réglages : Apparence | CommandCatalog.cs |
| Settings: Shortcuts | Réglages : Raccourcis | CommandCatalog.cs |
| Settings: Updates | Réglages : Mises à jour | CommandCatalog.cs |
| indexing {0}… {1} % | indexation de {0}… {1} % | PaletteIndex.cs |
| 1 item | 1 élément | PaletteIndex.cs |
| {0} items | {0} éléments | PaletteIndex.cs |
| Actions | Actions | PaletteSearch.cs |
| Suggested · {0} | Suggestions · {0} | PaletteSearch.cs |
| Device window | Fenêtre d’appareil | PaletteSearch.cs |
| MIDI effects need an instrument track | Les effets MIDI nécessitent une piste d’instrument | PaletteTargets.cs |
|  · keep open |  · garder ouvert | PaletteTargets.cs |
| track {0} | piste {0} | PaletteTargets.cs |
| ↵ run | ↵ exécuter | PaletteTargets.cs |
| → open {0} in Devices | → ouvrir {0} dans Appareils | PaletteTargets.cs |
| → select {0} in {1} | → sélectionner {0} dans {1} | PaletteTargets.cs |
| canvas, unconnected | canevas, non connecté | PaletteTargets.cs |
| → {0} · {1}{2} | → {0} · {1}{2} | PaletteTargets.cs |
| loads the kit | charge le kit | PaletteTargets.cs |
| → new track{0} | → nouvelle piste{0} | PaletteTargets.cs |
| → new MIDI track{0} | → nouvelle piste MIDI{0} | PaletteTargets.cs |
| instrument | instrument | PaletteTargets.cs |
| replaces {0} | remplace {0} | PaletteTargets.cs |
| new pad in Drum Rack | nouveau pad dans Drum Rack | PaletteTargets.cs |
| new chain in Instrument Rack | nouvelle chaîne dans Instrument Rack | PaletteTargets.cs |
| replaces the instrument | remplace l’instrument | PaletteTargets.cs |
| after {0} | après {0} | PaletteTargets.cs |
| end of Audio FX | fin des effets audio | PaletteTargets.cs |
| end of MIDI FX | fin des effets MIDI | PaletteTargets.cs |
| MIDI FX, end | effets MIDI, fin | PaletteTargets.cs |
| end of chain | fin de la chaîne | PaletteTargets.cs |
| MIDI · {0} | MIDI · {0} | PaletteProviders.cs |
| MIDI FX | Effets MIDI | PaletteProviders.cs |
| Audio FX | Effets audio | PaletteProviders.cs |
| Kits | Kits | PaletteProviders.cs |
| Modular · {0} | Modular · {0} | PaletteProviders.cs |

## Navigateur (90)

| English | Français | Source |
|---|---|---|
| Projects | Projets | BrowserView.cs |
| Presets | Préréglages | BrowserView.cs |
| Preset | Préréglage | BrowserView.cs |
| Audio effects | Effets audio | BrowserView.cs |
| MIDI effects | Effets MIDI | BrowserView.cs |
| Files | Fichiers | BrowserView.cs |
| MIDI map | Carte MIDI | BrowserView.cs |
| History | Historique | BrowserView.cs |
| Track inspector | Inspecteur de piste | BrowserView.cs |
| Collapse browser | Replier le navigateur | BrowserView.cs |
| Expand browser | Déplier le navigateur | BrowserView.cs |
| No matches yet | Aucun résultat pour l’instant | BrowserView.cs |
| Nota is still analysing your samples — more matches appear as it goes. | Nota analyse encore les échantillons — d’autres résultats apparaissent au fur et à mesure. | BrowserView.cs |
| No matches | Aucun résultat | BrowserView.cs |
| Try a different search or clear the filters. | Essayer une autre recherche ou effacer les filtres. | BrowserView.cs |
| No instruments yet | Aucun instrument pour l’instant | BrowserView.cs |
| No audio effects yet | Aucun effet audio pour l’instant | BrowserView.cs |
| Arpeggiator, chord, scale and more — they process notes before an instrument; drop one to the left of an instrument in a track. | Arpégiateur, accord, gamme et plus — ils traitent les notes avant un instrument : en déposer un à gauche d’un instrument dans une piste. | BrowserView.cs |
| No MIDI effects yet | Aucun effet MIDI pour l’instant | BrowserView.cs |
| Add audio files to your Samples folder (Settings → Library) or install free packs from Settings → Downloads → Sample packs, then browse them here as a folder tree. You can also drag files in from Finder. | Ajouter des fichiers audio au dossier d’échantillons (Réglages → Bibliothèque) ou installer des packs gratuits depuis Réglages → Téléchargements → Packs d’échantillons, puis les parcourir ici sous forme d’arborescence. Des fichiers peuvent aussi être glissés depuis le Finder. | BrowserView.cs |
| No samples yet | Aucun échantillon pour l’instant | BrowserView.cs |
| No presets yet | Aucun préréglage pour l’instant | BrowserView.cs |
| Right-click a device header and choose Save preset; it appears here grouped by category and device. | Clic droit sur l’en-tête d’un appareil puis « Enregistrer le préréglage » : il apparaît ici, groupé par catégorie et par appareil. | BrowserView.cs |
| No projects yet | Aucun projet pour l’instant | BrowserView.cs |
| Save a project (⌘S) into your Projects folder (Settings → Library) and it shows up here. | Enregistrer un projet (⌘S) dans le dossier Projets (Réglages → Bibliothèque) : il apparaît ici. | BrowserView.cs |
|  · analysing {0} % |  · analyse {0} % | BrowserView.cs |
| Collapse | Replier | BrowserView.cs |
| Expand | Déplier | BrowserView.cs |
| Sort and view | Tri et affichage | BrowserView.cs |
| Show type tags | Afficher les étiquettes de type | BrowserView.cs |
| Category chips | Pastilles de catégorie | BrowserView.cs |
| Group by source | Grouper par source | BrowserView.cs |
| Favorites first | Favoris en premier | BrowserView.cs |
| Warp loops to project tempo | Warper les boucles au tempo du projet | BrowserView.cs |
| Transpose to project key | Transposer à la tonalité du projet | BrowserView.cs |
| Edit tags… | Modifier les étiquettes… | BrowserView.cs |
| Favorites | Favoris | BrowserView.cs |
| Like {0} | Comme {0} | BrowserView.cs |
| Sounds like {0}, closest first — click to go back to all samples | Sons proches de {0}, les plus proches d’abord — cliquer pour revenir à tous les échantillons | BrowserView.cs |
| Filter by tempo | Filtrer par tempo | BrowserView.cs |
| Filter by key — a key also shows its relative (A minor ↔ C major) | Filtrer par tonalité — une tonalité montre aussi sa relative (A mineur ↔ C majeur) | BrowserView.cs |
| BPM | BPM | BrowserView.cs |
| Project tempo ±5 % | Tempo du projet ±5 % | BrowserView.cs |
| Loops within 5 % of {0} BPM warp to it cleanly | Les boucles à moins de 5 % de {0} BPM s’y calent proprement | BrowserView.cs |
| Any tempo | Tous les tempos | BrowserView.cs |
| Project key · {0} | Tonalité du projet · {0} | BrowserView.cs |
| Any key | Toutes les tonalités | BrowserView.cs |
| Finding sounds like {0}… | Recherche de sons proches de {0}… | BrowserView.cs |
| Couldn't analyse that sample | Impossible d’analyser cet échantillon | BrowserView.cs |
| Audio effect | Effet audio | BrowserView.cs |
| MIDI effect | Effet MIDI | BrowserView.cs |
| MIDI file | Fichier MIDI | BrowserView.cs |
| Project | Projet | BrowserView.cs |
| Folder | Dossier | BrowserView.cs |
| Add to favorites | Ajouter aux favoris | BrowserView.cs |
| Remove from favorites | Retirer des favoris | BrowserView.cs |
| Tags | Étiquettes | BrowserView.cs |
| New tag… | Nouvelle étiquette… | BrowserView.cs |
| Show similar sounds | Afficher les sons similaires | BrowserView.cs |
| Delete… | Supprimer… | BrowserView.cs |
| Create multisample… | Créer un multi-échantillon… | BrowserView.cs |
| Map the folder's samples by their names into Nota Mosaic presets | Assigner les échantillons du dossier selon leurs noms dans des préréglages Nota Mosaic | BrowserView.cs |
| Open | Ouvrir | BrowserView.cs |
| {0} built-in · {1} | {0} intégrés · {1} | BrowserView.cs |
| {0} plug-ins | {0} plug-in / {0} plug-ins | BrowserView.cs |
| {0} presets | {0} préréglage / {0} préréglages | BrowserView.cs |
| {0} projects | {0} projet / {0} projets | BrowserView.cs |
| External drop named no files Nota can read. Formats: {0} | Le dépôt externe ne contenait aucun fichier lisible par Nota. Formats : {0} | BrowserView.cs |
| Nota's built-in effects plus your VST/AU plug-ins. Scan your plug-ins in Settings → Plug-ins to add more. | Les effets intégrés de Nota et vos plug-ins VST/AU. Analyser vos plug-ins dans Réglages → Plug-ins pour en ajouter. | BrowserView.cs |
| Nota's built-in synths plus your VST/AU plug-ins. Scan your plug-ins in Settings → Plug-ins to add more. | Les synthés intégrés de Nota et vos plug-ins VST/AU. Analyser vos plug-ins dans Réglages → Plug-ins pour en ajouter. | BrowserView.cs |
| Load failed: {0} | Échec du chargement : {0} | MainWindow.Browser.cs |
| Reveal failed: {0} | Échec de l’affichage : {0} | MainWindow.Browser.cs |
| Delete project | Supprimer le projet | MainWindow.Browser.cs |
| Move “{0}” to the Trash? | Mettre « {0} » à la corbeille ? | MainWindow.Browser.cs |
| Couldn't delete {0} | Impossible de supprimer {0} | MainWindow.Browser.cs |
| Moved {0} to Trash | {0} mis à la corbeille | MainWindow.Browser.cs |
| Delete preset | Supprimer le préréglage | MainWindow.Browser.cs |
| Preset name | Nom du préréglage | MainWindow.Browser.cs |
| Save preset | Enregistrer le préréglage | MainWindow.Browser.cs |
| Nothing to save (built-in synth has no state). | Rien à enregistrer (le synthé intégré n’a pas d’état). | MainWindow.Browser.cs |
| Saved preset {0} | Préréglage {0} enregistré | MainWindow.Browser.cs |
| Save preset failed: {0} | Échec de l’enregistrement du préréglage : {0} | MainWindow.Browser.cs |
| Only plug-in chain instruments can be saved as presets. | Seuls les instruments à chaîne de plug-ins peuvent être enregistrés comme préréglages. | MainWindow.Browser.cs |
| Packs | Packs | BrowserViewModel.cs |
| Nota Kits | Nota Kits | BrowserViewModel.cs |
| sample packs installed from Settings → Downloads | packs d’échantillons installés depuis Réglages → Téléchargements | BrowserViewModel.cs |
| MIDI Effects | Effets MIDI | BrowserViewModel.cs |
| Audio Effects | Effets audio | BrowserViewModel.cs |
| BUILT-IN | Intégrés | BrowserViewModel.cs |
| PLUG-INS | Plug-ins | BrowserViewModel.cs |

## Navigateur et modèles de vue (24)

| English | Français | Source |
|---|---|---|
| Audio: driver settings changed · {0} frames | Audio : réglages du pilote modifiés · {0} échantillons | MainWindowViewModel.cs |
| ⚠ Audio dropout ({0}) | ⚠ Décrochage audio ({0}) | MainWindowViewModel.cs |
| Close the driver's settings window to apply the change | Fermer la fenêtre de réglages du pilote pour appliquer le changement | MainWindowViewModel.cs |
| Audio device error: {0} | Erreur du périphérique audio : {0} | MainWindowViewModel.cs |
| Audio: ASIO · {0} · {1:0} Hz · {2} frames | Audio : ASIO · {0} · {1:0} Hz · {2} échantillons | MainWindowViewModel.cs |
| WASAPI exclusive unavailable — using shared mode | WASAPI exclusif indisponible — mode partagé utilisé | MainWindowViewModel.cs |
| Audio: {0:0} Hz · {1} frames | Audio : {0:0} Hz · {1} échantillons | MainWindowViewModel.cs |
| Audio: {0:0} Hz | Audio : {0:0} Hz | MainWindowViewModel.cs |
| ASIO driver ‘{0}’ couldn't start — using WASAPI. {1} | Le pilote ASIO ‘{0}’ n’a pas pu démarrer — WASAPI utilisé. {1} | MainWindowViewModel.cs |
| No audio output could be opened — Nota runs without sound. Choose a device in Settings → Audio | Aucune sortie audio n’a pu être ouverte — Nota fonctionne sans son. Choisir un périphérique dans Réglages → Audio | MainWindowViewModel.cs |
| No audio output device opened — Nota runs silently. Choose a device in Settings → Audio | Aucun périphérique de sortie audio ouvert — Nota fonctionne en silence. Choisir un périphérique dans Réglages → Audio | MainWindowViewModel.cs |
| MIDI inputs updated. | Entrées MIDI mises à jour. | MainWindowViewModel.cs |
| MIDI error: {0} | Erreur MIDI : {0} | MainWindowViewModel.cs |
| First save | Premier enregistrement | VersionSummary.cs |
| First save · {0} tracks | Premier enregistrement · {0} piste / Premier enregistrement · {0} pistes | VersionSummary.cs |
| Added {0} | {0} ajouté | VersionSummary.cs |
| Removed {0} | {0} supprimé | VersionSummary.cs |
| Tempo {0} → {1} | Tempo {0} → {1} | VersionSummary.cs |
| New audio | Nouvel audio | VersionSummary.cs |
| {0} new audio files | {0} nouveaux fichiers audio | VersionSummary.cs |
| Edited {0} | {0} modifié | VersionSummary.cs |
| Devices on {0} | Appareils sur {0} | VersionSummary.cs |
| Mix of {0} | Mixage de {0} | VersionSummary.cs |
| Mix of {0} tracks | Mixage de {0} piste / Mixage de {0} pistes | VersionSummary.cs |

## Arrangement et pistes (109)

| English | Français | Source |
|---|---|---|
| Copy automation | Copier l’automation | ArrangementView.AutoEditor.cs |
| Cut automation | Couper l’automation | ArrangementView.AutoEditor.cs |
| Paste automation | Coller l’automation | ArrangementView.AutoEditor.cs |
| Delete automation | Supprimer l’automation | ArrangementView.AutoEditor.cs |
| Vol | Vol | ArrangementView.Automation.cs |
| Param | Param | ArrangementView.Automation.cs |
| Band {0} | Bande {0} | ArrangementView.Automation.cs |
| Stage {0} | Étape {0} | ArrangementView.Automation.cs |
| Choose parameter… | Choisir un paramètre… | ArrangementView.Automation.cs |
| Learn (touch a control in the plugin) | Apprendre (toucher une commande dans le plug-in) | ArrangementView.Automation.cs |
| Filter parameters… | Filtrer les paramètres… | ArrangementView.Automation.cs |
| Turn loop off | Désactiver la boucle | ArrangementView.Controls.cs |
| Turn loop on | Activer la boucle | ArrangementView.Controls.cs |
| Clear loop region | Effacer la région de boucle | ArrangementView.Controls.cs |
| click to add master volume points | cliquer pour ajouter des points de volume master | ArrangementView.Controls.cs |
| Unfold the group to move these clips across tracks | Déplier le groupe pour déplacer ces clips entre les pistes | ArrangementView.KeyMoves.cs |
| No track above takes these clips | Aucune piste au-dessus n’accepte ces clips | ArrangementView.KeyMoves.cs |
| No track below takes these clips | Aucune piste en dessous n’accepte ces clips | ArrangementView.KeyMoves.cs |
| Edit selected clips together | Éditer ensemble les clips sélectionnés | ArrangementView.LaneControl.cs |
| Loop selection | Boucler la sélection | ArrangementView.LaneControl.cs |
| Moved across tracks — device automation stayed on the source | Déplacé entre les pistes — l’automation des appareils est restée sur la source | ArrangementView.LaneControl.cs |
| Release to load the clip into {0} | Relâcher pour charger le clip dans {0} | ArrangementView.LaneControl.cs |
| Couldn't take the clip's audio: {0} | Impossible de récupérer l’audio du clip : {0} | ArrangementView.LaneControl.cs |
| Consolidate selection | Consolider la sélection | ArrangementView.LaneControl.cs |
| Audio clip | Clip audio | ArrangementView.LaneControl.cs |
| MIDI clip | Clip MIDI | ArrangementView.LaneControl.cs |
| Rename… | Renommer… | ArrangementView.LaneControl.cs |
| Split here | Découper ici | ArrangementView.LaneControl.cs |
| Reverse | Inverser | ArrangementView.LaneControl.cs |
| Activate | Activer | ArrangementView.LaneControl.cs |
| Deactivate | Désactiver | ArrangementView.LaneControl.cs |
| Separate stems… | Séparer les stems… | ArrangementView.LaneControl.cs |
| Convert | Convertir | ArrangementView.LaneControl.cs |
| Melody to new MIDI track | Mélodie vers une nouvelle piste MIDI | ArrangementView.LaneControl.cs |
| Harmony to new MIDI track | Harmonie vers une nouvelle piste MIDI | ArrangementView.LaneControl.cs |
| Drums to new MIDI track | Batterie vers une nouvelle piste MIDI | ArrangementView.LaneControl.cs |
| Slice to new MIDI track | Tranches vers une nouvelle piste MIDI | ArrangementView.LaneControl.cs |
| Get the AI model for melody and harmony… | Obtenir le modèle d’IA pour la mélodie et l’harmonie… | ArrangementView.LaneControl.cs |
| Copy to Session | Copier vers Session | ArrangementView.LaneControl.cs |
| Copy to scene | Copier vers la scène | ArrangementView.LaneControl.cs |
| Scene {0} | Scène {0} | ArrangementView.LaneControl.cs |
| Rec | Rec | ArrangementView.LaneControl.cs |
| Add an instrument from the browser | Ajouter un instrument depuis le navigateur | ArrangementView.LaneControl.cs |
| New track | Nouvelle piste | ArrangementView.LaneControl.cs |
| Couldn't move the clips to new tracks | Impossible de déplacer les clips vers de nouvelles pistes | ArrangementView.NewTrackDrop.cs |
| Moved to new tracks — device automation stayed on the source | Déplacé vers de nouvelles pistes — l’automation des appareils est restée sur la source | ArrangementView.NewTrackDrop.cs |
| Overview | Aperçu | ArrangementView.Overview.cs |
| bar {0}–{1} / {2} | mesure {0}–{1} / {2} | ArrangementView.Overview.cs |
| the project appears here | le projet apparaît ici | ArrangementView.Overview.cs |
| Sections | Sections | ArrangementView.Sections.cs |
| Automatic name | Nom automatique | ArrangementView.Sections.cs |
| Custom name | Nom personnalisé | ArrangementView.Sections.cs |
| Reset to default | Rétablir par défaut | ArrangementView.Sections.cs |
| Back to the automatic name ({0}), which follows the section's position | Retour au nom automatique ({0}), qui suit la position de la section | ArrangementView.Sections.cs |
| No section at the playhead | Aucune section à la tête de lecture | ArrangementView.Sections.cs |
| {0} already has its automatic name | {0} a déjà son nom automatique | ArrangementView.Sections.cs |
| “{0}” is {1} again | “{0}” redevient {1} | ArrangementView.Sections.cs |
| Reset to default name | Rétablir le nom par défaut | ArrangementView.Sections.cs |
| Loop section | Boucler la section | ArrangementView.Sections.cs |
| Select section | Sélectionner la section | ArrangementView.Sections.cs |
| Add section here | Ajouter une section ici | ArrangementView.Sections.cs |
| Delete all sections | Supprimer toutes les sections | ArrangementView.Sections.cs |
| drag here to mark a section | glisser ici pour marquer une section | ArrangementView.Sections.cs |
| Section · {0} bars | Section · {0} mesure / Section · {0} mesures | ArrangementView.Sections.cs |
| Take {0} | Prise {0} | ArrangementView.Takes.cs |
| Use whole take | Utiliser la prise entière | ArrangementView.Takes.cs |
| Flatten takes | Aplatir les prises | ArrangementView.Takes.cs |
| Delete take | Supprimer la prise | ArrangementView.Takes.cs |
| Hide takes | Masquer les prises | ArrangementView.Takes.cs |
| Show {0} takes | Afficher {0} prises | ArrangementView.Takes.cs |
| Track | Piste | ArrangementView.Tracks.cs |
| Group tracks | Grouper les pistes | ArrangementView.Tracks.cs |
| Track {0} | Piste {0} | ArrangementView.Tracks.cs |
| {0} tracks | {0} pistes | ArrangementView.Tracks.cs |
| {0} bars | {0} mesure / {0} mesures | ArrangementView.cs |
| {0:0.##} beats | {0:0.##} temps | ArrangementView.cs |
| Processing… | Traitement… | ArrangementView.cs |
| Paste a MIDI clip onto a MIDI track, audio onto audio. | Coller un clip MIDI sur une piste MIDI, de l’audio sur de l’audio. | ArrangementView.cs |
| Copied clip to session scene {0} | Clip copié vers la scène {0} de la session | ArrangementView.cs |
| Copied {0} clips to the session from scene {1} | {0} clips copiés vers la session à partir de la scène {1} | ArrangementView.cs |
| Add instrument track | Ajouter une piste d’instrument | ArrangementView.cs |
| Add audio track | Ajouter une piste audio | ArrangementView.cs |
| Add return track | Ajouter une piste de retour | ArrangementView.cs |
| Paste track | Coller la piste | ArrangementView.cs |
| Add track | Ajouter une piste | ArrangementView.cs |
| Ungroup | Dégrouper | ArrangementView.cs |
| In | Entrée | ArrangementView.cs |
| Monitor input: hear it live through this track | Écoute de l’entrée : l’entendre en direct à travers cette piste | ArrangementView.cs |
| Monitoring input — click to stop | Écoute de l’entrée — cliquer pour arrêter | ArrangementView.cs |
| Played from a phone: {0} | Joué depuis un téléphone : {0} | ArrangementView.cs |
| Group | Groupe | TrackInspectorView.cs |
| Compact | Compact | TrackInspectorView.cs |
| Large | Grand | TrackInspectorView.cs |
| Standard | Standard | TrackInspectorView.cs |
| Height | Hauteur | TrackInspectorView.cs |
| MIDI from | MIDI depuis | TrackInspectorView.cs |
| Master can't be monitored (it would feed back) | Le master ne peut pas être écouté (il y aurait un larsen) | TrackInspectorView.cs |
| No track selected | Aucune piste sélectionnée | TrackInspectorView.cs |
| Audio track | Piste audio | TrackInspectorView.cs |
| MIDI track | Piste MIDI | TrackInspectorView.cs |
| Name | Nom | TrackInspectorView.cs |
| Mute the track | Mettre la piste en sourdine | TrackInspectorView.cs |
| Solo the track | Mettre la piste en solo | TrackInspectorView.cs |
| Arm | Armer | TrackInspectorView.cs |
| Hear the input live through this track | Entendre l’entrée en direct à travers cette piste | TrackInspectorView.cs |
| Monitor input | Écoute de l’entrée | TrackInspectorView.cs |
| Play another instrument track's MIDI through this track's instrument | Jouer le MIDI d’une autre piste d’instrument avec l’instrument de cette piste | TrackInspectorView.cs |
| The colour the track's position gives it | La couleur que lui donne la position de la piste | TrackInspectorView.cs |
| Return | Retour | TrackInspectorView.cs |

## Session (153)

| English | Français | Source |
|---|---|---|
| Launch scene | Lancer la scène | SessionView.Grid.cs |
| Stop all clips | Arrêter tous les clips | SessionView.Grid.cs |
| Scenes | Scènes | SessionView.Grid.cs |
| + Scene | + Scène | SessionView.Grid.cs |
| Add a scene at the end | Ajouter une scène à la fin | SessionView.Grid.cs |
| Stop every track in this group | Arrêter toutes les pistes de ce groupe | SessionView.Grid.cs |
| Stop this track | Arrêter cette piste | SessionView.Grid.cs |
| Fold the group into one column | Replier le groupe en une colonne | SessionView.Grid.cs |
| Show the group's tracks | Afficher les pistes du groupe | SessionView.Grid.cs |
| Arr | Arr | SessionView.Grid.cs |
| Back to Arrangement for this track | Retour à l’arrangement pour cette piste | SessionView.Grid.cs |
| Drop a MIDI clip on a MIDI track, audio on audio. | Déposer un clip MIDI sur une piste MIDI, de l’audio sur de l’audio. | SessionView.Grid.cs |
| Rec in… | Rec dans… | SessionView.Grid.cs |
| Moved scene | Scène déplacée | SessionView.Grid.cs |
| {0} · scene {1} | {0} · scène {1} | SessionView.Grid.cs |
| Copy to Arrangement | Copier vers l’arrangement | SessionView.Grid.cs |
| Add stop button | Ajouter un bouton d’arrêt | SessionView.Grid.cs |
| Remove stop button | Retirer le bouton d’arrêt | SessionView.Grid.cs |
| Delete group and its tracks | Supprimer le groupe et ses pistes | SessionView.Grid.cs |
| Scene · {0} | Scène · {0} | SessionView.Grid.cs |
| Stop scene | Arrêter la scène | SessionView.Grid.cs |
| Insert scene below | Insérer une scène dessous | SessionView.Grid.cs |
| Capture playing clips below | Capturer les clips en lecture dessous | SessionView.Grid.cs |
| Copy scene to Arrangement | Copier la scène vers l’arrangement | SessionView.Grid.cs |
| Delete scene | Supprimer la scène | SessionView.Grid.cs |
| Duplicate | Dupliquer | SessionView.Inspector.cs |
| Off | Désactivé | SessionView.Inspector.cs |
| Audio | Audio | SessionView.Inspector.cs |
| Last | Dernier | SessionView.Inspector.cs |
| Stop | Arrêt | SessionView.Inspector.cs |
| Insert MIDI clip | Insérer un clip MIDI | SessionView.Inspector.cs |
| {0} clips | {0} clips | SessionView.Inspector.cs |
| None | Aucune | SessionView.Inspector.cs |
| Length | Longueur | SessionView.Inspector.cs |
| Start | Début | SessionView.Inspector.cs |
| Tempo | Tempo | SessionView.Inspector.cs |
| Quantize | Quantifier | SessionView.Inspector.cs |
| Global | Global | SessionView.Inspector.cs |
| Gate | Gate | SessionView.Inspector.cs |
| Recording | Enregistrement | SessionView.Inspector.cs |
| Launch | Lancer | SessionView.Inspector.cs |
| Record here | Enregistrer ici | SessionView.Inspector.cs |
| Repeat | Répéter | SessionView.Inspector.cs |
| Toggle | Bascule | SessionView.Inspector.cs |
| Trigger | Déclencheur | SessionView.Inspector.cs |
| Again | Encore | SessionView.Inspector.cs |
| Next | Suivant | SessionView.Inspector.cs |
| First | Premier | SessionView.Inspector.cs |
| Previous | Précédent | SessionView.Inspector.cs |
| Any | Quelconque | SessionView.Inspector.cs |
| Other | Autre | SessionView.Inspector.cs |
| Jump | Saut | SessionView.Inspector.cs |
| One pass | Un passage | SessionView.Inspector.cs |
| Click selects · the triangle launches · Enter launches the selection⏎ ←↑↓→ move · ⌘C ⌘V ⌘D · ⌫ delete · ⌘I insert scene⏎ ⌘⇧C copy to arrangement · ⌘V pastes clips copied there | Clic sélectionne · le triangle lance · Entrée lance la sélection⏎ ←↑↓→ déplacer · ⌘C ⌘V ⌘D · ⌫ supprimer · ⌘I insérer une scène⏎ ⌘⇧C copier vers l’arrangement · ⌘V colle les clips copiés là | SessionView.Inspector.cs |
| Click again to use the default colour | Cliquer de nouveau pour utiliser la couleur par défaut | SessionView.Inspector.cs |
| Color | Couleur | SessionView.Inspector.cs |
| Off · one-shot | Désactivé · one-shot | SessionView.Inspector.cs |
| Loop end | Fin de boucle | SessionView.Inspector.cs |
| Trigger starts · Gate plays while held · Toggle starts and stops · Repeat retriggers while held | Déclencheur démarre · Gate joue tant que tenu · Bascule démarre et arrête · Répéter redéclenche tant que tenu | SessionView.Inspector.cs |
| Legato | Legato | SessionView.Inspector.cs |
| Velocity amount: how far the launch velocity scales the clip · drag | Quantité de vélocité : dans quelle mesure la vélocité de lancement met le clip à l’échelle · glisser | SessionView.Inspector.cs |
| Velocity % | Vélocité % | SessionView.Inspector.cs |
| Follow action | Action de suivi | SessionView.Inspector.cs |
| Off globally | Désactivé globalement | SessionView.Inspector.cs |
| Chance of A in percent · B gets the rest · drag | Probabilité de A en pourcentage · B reçoit le reste · glisser | SessionView.Inspector.cs |
| Jump to | Sauter à | SessionView.Inspector.cs |
| After | Après | SessionView.Inspector.cs |
| Rename | Renommer | SessionView.Inspector.cs |
| To Arrangement | Vers l’arrangement | SessionView.Inspector.cs |
| Launch without a tempo change | Lancer sans changement de tempo | SessionView.Inspector.cs |
| Tempo set when the scene launches · drag to change | Tempo réglé au lancement de la scène · glisser pour le changer | SessionView.Inspector.cs |
| Signature | Signature | SessionView.Inspector.cs |
| Next · {0} | Suivant · {0} | SessionView.Inspector.cs |
| Next after {0} | Suivant après {0} | SessionView.Inspector.cs |
| Select next scene on launch | Sélectionner la scène suivante au lancement | SessionView.Inspector.cs |
| Insert below | Insérer dessous | SessionView.Inspector.cs |
| Empty slot | Emplacement vide | SessionView.Inspector.cs |
| Removed | Retiré | SessionView.Inspector.cs |
| Stop button | Bouton d’arrêt | SessionView.Inspector.cs |
| Launching this scene keeps {0} playing. | Le lancement de cette scène laisse {0} en lecture. | SessionView.Inspector.cs |
| Launching this scene stops {0}. | Le lancement de cette scène arrête {0}. | SessionView.Inspector.cs |
| Stop recording | Arrêter l’enregistrement | SessionView.Inspector.cs |
| Every track ends up here. Its devices are in the Devices panel. | Toutes les pistes aboutissent ici. Ses appareils sont dans le panneau Appareils. | SessionView.Inspector.cs |
| Tracks reach it through their sends. It has no clip slots; its devices are in the Devices panel. | Les pistes l’atteignent par leurs envois. Il n’a pas d’emplacements de clips ; ses appareils sont dans le panneau Appareils. | SessionView.Inspector.cs |
| GROUP SLOT · {0} TRACKS | EMPLACEMENT DE GROUPE · {0} PISTES | SessionView.Inspector.cs |
| Launches every track's slot in this scene. The stop button stops them all. | Lance l’emplacement de chaque piste dans cette scène. Le bouton d’arrêt les arrête tous. | SessionView.Inspector.cs |
| Launch group slot | Lancer l’emplacement du groupe | SessionView.Inspector.cs |
| Stop group | Arrêter le groupe | SessionView.Inspector.cs |
| 1 bar | 1 mesure | SessionView.Inspector.cs |
| 1 beat | 1 temps | SessionView.Inspector.cs |
| 16 bars | 16 mesures | SessionView.Inspector.cs |
| 2 bars | 2 mesures | SessionView.Inspector.cs |
| 2 beats | 2 temps | SessionView.Inspector.cs |
| 4 bars | 4 mesures | SessionView.Inspector.cs |
| 8 bars | 8 mesures | SessionView.Inspector.cs |
| SCENE {0:00} · {1} | SCÈNE {0:00} · {1} | SessionView.Inspector.cs |
| Auto | Auto | SessionView.Mixer.cs |
| Input | Entrée | SessionView.Mixer.cs |
| Output | Sortie | SessionView.Mixer.cs |
| Pan | Pan | SessionView.Mixer.cs |
| Volume | Volume | SessionView.Mixer.cs |
| Record input | Entrée d’enregistrement | SessionView.Mixer.cs |
| Mute | Muet | SessionView.Mixer.cs |
| Solo | Solo | SessionView.Mixer.cs |
| Monitor | Écoute | SessionView.Mixer.cs |
| Send {0} · {1} | Envoi {0} · {1} | SessionView.Mixer.cs |
| Send | Envoi | SessionView.Mixer.cs |
| All ins | Toutes les entrées | SessionView.Mixer.cs |
| MIDI input: every input, or another instrument track | Entrée MIDI : toutes les entrées, ou une autre piste d’instrument | SessionView.Mixer.cs |
| In: hear the input live through this track · Auto: only while recording | Entrée : entendre l’entrée en direct à travers cette piste · Auto : seulement pendant l’enregistrement | SessionView.Mixer.cs |
| Output — group a track to route it through the group | Sortie — grouper une piste pour la router à travers le groupe | SessionView.Mixer.cs |
| Show | Afficher | SessionView.Toolbar.cs |
| Stop every session clip | Arrêter tous les clips de la session | SessionView.Toolbar.cs |
| Session rec | Rec session | SessionView.Toolbar.cs |
| Record into the armed track at the selected scene · click again to keep the take | Enregistrer dans la piste armée à la scène sélectionnée · cliquer de nouveau pour garder la prise | SessionView.Toolbar.cs |
| Fixed | Fixe | SessionView.Toolbar.cs |
| Fixed record length in bars — the take stops by itself | Durée d’enregistrement fixe en mesures — la prise s’arrête d’elle-même | SessionView.Toolbar.cs |
| Quant | Quant | SessionView.Toolbar.cs |
| Launch quantize: when clips and scenes start | Quantification du lancement : quand les clips et les scènes démarrent | SessionView.Toolbar.cs |
| Follow | Suivi | SessionView.Toolbar.cs |
| Follow actions on or off for every clip and scene | Actions de suivi activées ou désactivées pour chaque clip et chaque scène | SessionView.Toolbar.cs |
| Capture scene | Capturer la scène | SessionView.Toolbar.cs |
| Copy the clips playing now into a new scene | Copier les clips en lecture dans une nouvelle scène | SessionView.Toolbar.cs |
| Back to Arrangement | Retour à l’arrangement | SessionView.Toolbar.cs |
| Stop session clips and hand every track back to the Arrangement | Arrêter les clips de la session et rendre chaque piste à l’arrangement | SessionView.Toolbar.cs |
| Stop all | Tout arrêter | SessionView.Toolbar.cs |
| Inspector — show or hide it | Inspecteur — l’afficher ou le masquer | SessionView.Toolbar.cs |
| Inspector | Inspecteur | SessionView.Toolbar.cs |
| More session controls | Plus de commandes de session | SessionView.Toolbar.cs |
| Hide | Masquer | SessionView.Toolbar.cs |
| Turn follow off | Désactiver le suivi | SessionView.Toolbar.cs |
| Turn follow on | Activer le suivi | SessionView.Toolbar.cs |
| Arm a track with an empty slot in this scene to record. | Armer une piste avec un emplacement vide dans cette scène pour enregistrer. | SessionView.Toolbar.cs |
| 1 Bar | 1 mesure | SessionView.Toolbar.cs |
| 2 Bars | 2 mesures | SessionView.Toolbar.cs |
| 4 Bars | 4 mesures | SessionView.Toolbar.cs |
| 8 Bars | 8 mesures | SessionView.Toolbar.cs |
| Moved clip | Clip déplacé | SessionView.cs |
| Group · {0}{1} | Groupe · {0}{1} | SessionView.cs |
| Cut clip — paste to move it | Clip coupé — coller pour le déplacer | SessionView.cs |
| Pasted clip from the arrangement | Clip de l’arrangement collé | SessionView.cs |
| Pasted {0} clips from the arrangement | {0} clips de l’arrangement collés | SessionView.cs |
| Duplicated scene | Scène dupliquée | SessionView.cs |
| The slot below is taken. | L’emplacement du dessous est occupé. | SessionView.cs |
| Duplicated clip | Clip dupliqué | SessionView.cs |
| Deleted scene | Scène supprimée | SessionView.cs |
| Deleted clip | Clip supprimé | SessionView.cs |
| Inserted scene | Scène insérée | SessionView.cs |
| Captured | Capturé | SessionView.cs |
| Captured the playing clips into a new scene | Clips en lecture capturés dans une nouvelle scène | SessionView.cs |
| Copied clip to the arrangement at the playhead | Clip copié dans l’arrangement à la tête de lecture | SessionView.cs |
| Copied {0} clips to the arrangement at the playhead | {0} clips copiés dans l’arrangement à la tête de lecture | SessionView.cs |
| {0}.{1} bars | {0}.{1} mesures | SessionView.cs |

## Mixeur (20)

| English | Français | Source |
|---|---|---|
| Group in | Entrée du groupe | MixerView.cs |
| → Master | → Master | MixerView.cs |
| Monitor auto | Écoute auto | MixerView.cs |
| Sum bus | Bus de somme | MixerView.cs |
| → Output | → Sortie | MixerView.cs |
| Hidden: not enough height · click to show anyway | Masqué : hauteur insuffisante · cliquer pour afficher quand même | MixerView.cs |
| Pan law | Loi de panoramique | MixerView.cs |
| Panning attenuates the opposite channel (legacy behaviour) | Le panoramique atténue le canal opposé (comportement historique) | MixerView.cs |
| Panning moves the whole stereo image; nothing is dropped | Le panoramique déplace toute l’image stéréo ; rien n’est supprimé | MixerView.cs |
| Older project | Ancien projet | MixerView.cs |
| Projects saved before stereo pan law open with Balance so existing mixes do not change | Les projets enregistrés avant la loi de panoramique stéréo s’ouvrent avec Balance, pour que les mixages existants ne changent pas | MixerView.cs |
| Pan moves the whole image of stereo tracks and returns | Le pan déplace toute l’image des pistes stéréo et des retours | MixerView.cs |
| Pan attenuates the opposite channel, as in earlier versions | Le pan atténue le canal opposé, comme dans les versions précédentes | MixerView.cs |
| Track Pan | Pan de la piste | MixerView.cs |
| Groups keep a unity-centre balance so grouping stays level-transparent | Les groupes gardent une balance centrée à gain unitaire, pour que le groupage reste transparent en niveau | MixerView.cs |
| Set the gain — drag, or double-click to type | Régler le gain : glisser, ou double-clic pour le saisir | MixerView.cs |
| Track Volume | Volume de la piste | MixerView.cs |
| Return in | Entrée de retour | MixerView.cs |
| In 1 | Entrée 1 | MixerView.cs |
| Dock the mixer in the main window | Ancrer le mixeur dans la fenêtre principale | MixerWindow.cs |

## Modulaire (96)

| English | Français | Source |
|---|---|---|
| Window | Fenêtre | ModularView.Scope.cs |
| Zoom | Zoom | ModularView.Scope.cs |
| Watches CV. Drag a modulator's CV out onto this node. For audio, put Lens on the chain. | Surveille le CV. Glisser la sortie CV d’un modulateur sur ce nœud. Pour l’audio, mettre Lens sur la chaîne. | ModularView.Scope.cs |
| Drag to resize the trace | Glisser pour redimensionner la trace | ModularView.Scope.cs |
| The window is in beats at the tempo. Click for milliseconds | La fenêtre est en temps, au tempo. Cliquer pour passer en millisecondes | ModularView.Scope.cs |
| The window is in milliseconds. Click to sync it to the beat | La fenêtre est en millisecondes. Cliquer pour la synchroniser au tempo | ModularView.Scope.cs |
| Free running: the newest value is at the right | Défilement libre : la valeur la plus récente est à droite | ModularView.Scope.cs |
| Rise | Montée | ModularView.Scope.cs |
| Trigger: start the trace where the input rises through 0, so a repeating shape holds still | Déclencheur : démarrer la trace là où l’entrée franchit 0 en montant, pour qu’une forme répétée reste immobile | ModularView.Scope.cs |
| Back to the live trace | Retour à la trace en direct | ModularView.Scope.cs |
| Hold the trace while the input keeps running | Figer la trace pendant que l’entrée continue | ModularView.Scope.cs |
| Scope {0} · {1} | Scope {0} · {1} | ModularView.Scope.cs |
| Smooth | Lisse | ModularView.cs |
| Tracks | Pistes | ModularView.cs |
| Gain | Gain | ModularView.cs |
| Attack | Attaque | ModularView.cs |
| Decay | Déclin | ModularView.cs |
| Sustain | Maintien | ModularView.cs |
| Release | Relâchement | ModularView.cs |
| Audio {0} | Audio {0} | ModularView.cs |
| Fit | Ajuster | ModularView.cs |
| Scale | Gamme | ModularView.cs |
| Env | Env | ModularView.cs |
| Invert | Inverser | ModularView.cs |
| Min | Min | ModularView.cs |
| Max | Max | ModularView.cs |
| Add | Ajouter | ModularView.cs |
| MIDI in | Entrée MIDI | ModularView.cs |
| Sync | Sync | ModularView.cs |
| Free | Libre | ModularView.cs |
| Pick a track to see its signal graph | Choisir une piste pour voir son graphe de signal | ModularView.cs |
| Drag from a CV out onto a node to modulate it · Space + drag — pan · ⌘ + wheel — zoom · F — fit · Esc — show fewer | Glisser d’une sortie CV sur un nœud pour le moduler · Space + glisser — déplacer la vue · ⌘ + molette — zoom · F — ajuster · Esc — afficher moins | ModularView.cs |
| No tracks yet | Aucune piste pour l’instant | ModularView.cs |
| Inst {0} | Inst {0} | ModularView.cs |
| Instrument {0} | Instrument {0} | ModularView.cs |
| Audio in | Entrée audio | ModularView.cs |
| Track output | Sortie de piste | ModularView.cs |
| {0:0.0} kHz · {1} · {2:0.0} ms | {0:0.0} kHz · {1} · {2:0.0} ms | ModularView.cs |
| {0} NODES | {0} NŒUDS | ModularView.cs |
| Out | Sortie | ModularView.cs |
| LFO {0} | LFO {0} | ModularView.cs |
| Macro | Macro | ModularView.cs |
| Math | Math | ModularView.cs |
| Scope | Scope | ModularView.cs |
| Depth | Depth | ModularView.cs |
| Phase | Phase | ModularView.cs |
| Multiply | Multiply | ModularView.cs |
| Subtract | Subtract | ModularView.cs |
| Offset | Offset | ModularView.cs |
| CV out | CV out | ModularView.cs |
| Note | Note | ModularView.cs |
| Vel | Vel | ModularView.cs |
| Rnd | Rnd | ModularView.cs |
| Saw | Saw | ModularView.cs |
| Sine | Sine | ModularView.cs |
| Sqr | Sqr | ModularView.cs |
| Tri | Tri | ModularView.cs |
| Rate | Rate | ModularView.cs |
| Parameter | Paramètre | ModularView.cs |
| Remove modulation | Retirer la modulation | ModularView.cs |
| From {0} · {1} | Depuis {0} · {1} | ModularView.cs |
| Modulate from a parameter | Moduler depuis un paramètre | ModularView.cs |
| Modulate from another track | Moduler depuis une autre piste | ModularView.cs |
| Add an LFO / expose a param first | Ajouter d’abord un LFO / exposer un paramètre | ModularView.cs |
| Expose as CV output | Exposer comme sortie CV | ModularView.cs |
| Unexpose CV output | Ne plus exposer la sortie CV | ModularView.cs |
| Remove connection | Retirer la connexion | ModularView.cs |
| CV LINK · LFO {0} | LIEN CV · LFO {0} | ModularView.cs |
| Override | Forcer | ModularView.cs |
| Mode | Mode | ModularView.cs |
| Bypass (B) | Contourner (B) | ModularView.cs |
| Delete (⌫) | Supprimer (⌫) | ModularView.cs |
| LFO | LFO | ModularView.cs |
| Open in a window | Ouvrir dans une fenêtre | ModularView.cs |
| Show fewer | Afficher moins | ModularView.cs |
| Hide the parameters that aren't modulated (Esc) | Masquer les paramètres non modulés (Esc) | ModularView.cs |
| Audio out | Sortie audio | ModularView.cs |
| MIDI out | Sortie MIDI | ModularView.cs |
| No parameters | Aucun paramètre | ModularView.cs |
| No modulatable parameters on that track | Aucun paramètre modulable sur cette piste | ModularView.cs |
| Fixed — the track's clips and input enter the chain here, so it can't be deleted. | Fixe — les clips et l’entrée de la piste entrent dans la chaîne ici, donc il ne peut pas être supprimé. | ModularView.cs |
| Fixed — the chain leaves the track for the mixer here, so it can't be deleted. | Fixe — la chaîne quitte la piste vers le mixeur ici, donc il ne peut pas être supprimé. | ModularView.cs |
| Fixed — the track's instrument takes its MIDI in. It can't be deleted; drop another instrument on the track to swap it. | Fixe — l’instrument de la piste reçoit son MIDI ici. Il ne peut pas être supprimé ; déposer un autre instrument sur la piste pour le remplacer. | ModularView.cs |
| Show all parameters | Afficher tous les paramètres | ModularView.cs |
| Show fewer parameters (Esc) | Afficher moins de paramètres (Esc) | ModularView.cs |
| + Add | + Ajouter | ModularView.cs |
| {0} edges | {0} lien / {0} liens | ModularView.cs |
| {0} nodes | {0} nœuds | ModularView.cs |
| {0} · {1} · {2} | {0} · {1} · {2} | ModularView.cs |
| Edit modulation · {0}… | Modifier la modulation · {0}… | ModularView.cs |
| Global · {0} | Global · {0} | ModularView.cs |
| Modulate with {0} {1} | Moduler avec {0} {1} | ModularView.cs |
| {0} cross-track CV links | {0} lien CV entre pistes / {0} liens CV entre pistes | ModularView.cs |
| {0} devices → | {0} appareil → / {0} appareils → | ModularView.cs |
| {0} islands | {0} îlot / {0} îlots | ModularView.cs |
|  · {0} |  · {0} | ModularView.cs |

## Éditeurs (piano roll, clips) (115)

| English | Français | Source |
|---|---|---|
| Beats | Beats | AudioClipEditorView.cs |
| Complex | Complex | AudioClipEditorView.cs |
| Complex Pro | Complex Pro | AudioClipEditorView.cs |
| Texture | Texture | AudioClipEditorView.cs |
| Tones | Tones | AudioClipEditorView.cs |
| Playback | Lecture | AudioClipEditorView.cs |
| Warp | Warp | AudioClipEditorView.cs |
| Detect tempo | Détecter le tempo | AudioClipEditorView.cs |
| Detect the file's tempo and conform it to the project | Détecter le tempo du fichier et l’adapter au projet | AudioClipEditorView.cs |
| To transients | Aux transitoires | AudioClipEditorView.cs |
| Pin a marker on every hit so drums lock to the grid | Placer un marqueur sur chaque attaque pour caler la batterie sur la grille | AudioClipEditorView.cs |
| Detected | Détecté | AudioClipEditorView.cs |
| Reset | Réinitialiser | AudioClipEditorView.cs |
| Play the whole sample at full level | Jouer tout l’échantillon à plein niveau | AudioClipEditorView.cs |
| Envelope | Enveloppe | AudioClipEditorView.cs |
| Stereo | Stéréo | AudioClipEditorView.cs |
| off | désactivé | AudioClipEditorView.cs |
| The tempo the audio was recorded at. Nota stretches it to the project tempo. ×2 / ÷2 fix a half- or double-time detection. | Le tempo auquel l’audio a été enregistré. Nota l’étire jusqu’au tempo du projet. ×2 / ÷2 corrigent une détection en demi-temps ou en double temps. | AudioClipEditorView.cs |
| Half the tempo — the clip spans half as many beats | Moitié du tempo — le clip couvre deux fois moins de temps | AudioClipEditorView.cs |
| Double the tempo — the clip spans twice as many beats | Double du tempo — le clip couvre deux fois plus de temps | AudioClipEditorView.cs |
| Click — point · drag — move · right-click — delete | Clic — point · glisser — déplacer · clic droit — supprimer | AudioClipEditorView.cs |
| Drag a handle · double-click — reset | Glisser une poignée · double-clic — réinitialiser | AudioClipEditorView.cs |
| Snap needs Warp — turn on Warp to see the grid and snap to it | Le magnétisme nécessite Warp — activer Warp pour voir la grille et s’y caler | AudioClipEditorView.cs |
| Double-click the wave — marker · drag — align | Double-clic sur la forme d’onde — marqueur · glisser — aligner | AudioClipEditorView.cs |
| {0} markers | {0} repère / {0} repères | AudioClipEditorView.cs |
| Tools | Outils | ClipEditorView.cs |
| Envelopes | Enveloppes | ClipEditorView.cs |
| Clip: the selected clip · Track: every clip on the track | Clip : le clip sélectionné · Piste : tous les clips de la piste | ClipEditorView.cs |
| Hear notes as you place, click or drag them | Entendre les notes quand on les place, les clique ou les fait glisser | ClipEditorView.cs |
| Fit to notes (⇧F) | Ajuster aux notes (⇧F) | ClipEditorView.cs |
| Remove out of scale | Supprimer hors gamme | ClipEditorView.cs |
| Delete every note outside the scale | Supprimer toutes les notes hors de la gamme | ClipEditorView.cs |
| Fold | Replier | ClipEditorView.cs |
| only the notes in use | seulement les notes utilisées | ClipEditorView.cs |
| only the pads | seulement les pads | ClipEditorView.cs |
| only the scale's notes | seulement les notes de la gamme | ClipEditorView.cs |
| Fold: show {0} (⌥⇧F) | Replier : afficher {0} (⌥⇧F) | ClipEditorView.cs |
| Click to add a point · drag to move · right-click to delete | Clic pour ajouter un point · glisser pour déplacer · clic droit pour supprimer | ClipEditorView.cs |
| Click to add a point | Clic pour ajouter un point | ClipEditorView.cs |
| Pitch | Hauteur | ClipPropsView.cs |
| Notes | Notes | ClipPropsView.cs |
| Velocity | Vélocité | ClipPropsView.cs |
| Clips always loop | Les clips bouclent toujours | ClipPropsView.cs |
| Strength | Intensité | ClipPropsView.cs |
| Transpose | Transposer | ClipPropsView.cs |
| No notes selected | Aucune note sélectionnée | ClipPropsView.cs |
| Selection | Sélection | ClipPropsView.cs |
| Double-click — add or delete · edge — length · Shift — add to selection | Double-clic — ajouter ou supprimer · bord — longueur · Shift — ajouter à la sélection | ClipPropsView.cs |
| beat | temps | ClipPropsView.cs |
| beats | temps | ClipPropsView.cs |
| Range | Plage | ClipPropsView.cs |
| Arm the next Single capture | Armer la prochaine capture Single | LensDeviceBody.cs |
| Export the Lens capture to WAV | Exporter la capture de Lens en WAV | LensDeviceBody.cs |
| Freeze — stop capturing and hold what is on screen | Freeze — arrête la capture et conserve ce qui est à l’écran | LensDeviceBody.cs |
| Key gain — scales only the key's trace, for the eye (the sound is untouched) | Key gain — ne met à l’échelle que la trace de la clé, pour l’œil (le son n’est pas modifié) | LensDeviceBody.cs |
| Key — draw another track's audio over this one, in teal | Key — superposer en turquoise l’audio d’une autre piste à celui-ci | LensDeviceBody.cs |
| Open Lens in its own window | Ouvrir Lens dans sa propre fenêtre | LensDeviceBody.cs |
| Show the key's trace | Afficher la trace de la clé | LensDeviceBody.cs |
| Span — how much of the song the scope shows, from a quarter beat to four bars | Span — la portion du morceau affichée par l’oscilloscope, d’un quart de temps à quatre mesures | LensDeviceBody.cs |
| Sync — line the scope up with the song's beats and bars | Sync — aligne l’oscilloscope sur les temps et les mesures du morceau | LensDeviceBody.cs |
| Vpp {0:0.000} · RMS {1:0.000} | Vpp {0:0.000} · RMS {1:0.000} | LensDeviceBody.cs |
| Waterfall · {0} bins · {1} · overlap {2:0.#} % · window {3:0} s · floor {4:0} dB | Waterfall · {0} bins · {1} · overlap {2:0.#} % · fenêtre {3:0} s · plancher {4:0} dB | LensDeviceBody.cs |
| Write the captured scope window to a WAV file | Écrire la fenêtre d’oscilloscope capturée dans un fichier WAV | LensDeviceBody.cs |
| crest {0:0.0} dB · LUFS {1:0.0} | crête {0:0.0} dB · LUFS {1:0.0} | LensDeviceBody.cs |
| level {0:0.00} · holdoff {1:0} ms | niveau {0:0.00} · holdoff {1:0} ms | LensDeviceBody.cs |
| move over the graph | survoler le graphique | LensDeviceBody.cs |
| n/a in a rack | indisponible dans un rack | LensDeviceBody.cs |
| peak {0} · {1:0.0} dB | crête {0} · {1:0.0} dB | LensDeviceBody.cs |
| period {0:0.000} ms · {1:0.0} Hz | période {0:0.000} ms · {1:0.0} Hz | LensDeviceBody.cs |
| row {0:0} ms · {1} bins | ligne {0:0} ms · {1} bins | LensDeviceBody.cs |
| window {0:0} ms · slope {1:0.0} dB/oct | fenêtre {0:0} ms · pente {1:0.0} dB/oct | LensDeviceBody.cs |
| {0:0.0} dB · bin {1:0} | {0:0.0} dB · bin {1:0} | LensDeviceBody.cs |
| Mid / Side | Mid / Side | LensDeviceBody.cs |
| Snap to peaks | Caler sur les crêtes | LensDeviceBody.cs |
| Bars | Barres | DrumPatternView.cs |
| No pads loaded | Aucun pad chargé | DrumPatternView.cs |
| Drop samples on the Drum Rack pads in Devices, then step them here. | Déposer des échantillons sur les pads du Drum Rack dans Appareils, puis les programmer ici pas à pas. | DrumPatternView.cs |
| click to place · shift-click accents · drag to paint | clic pour placer · ⇧-clic pour les accents · glisser pour peindre | DrumPatternView.cs |
| Clear | Effacer | DrumPatternView.cs |
| Play {0} · {1} and show its velocity lane | Jouer {0} · {1} et afficher son couloir de vélocité | DrumPatternView.cs |
| {0} pads | {0} pad / {0} pads | DrumPatternView.cs |
| {0} · {1} | {0} · {1} | DrumPatternView.cs |
| {0} steps | {0} pas / {0} pas | DrumPatternView.cs |
| Nota Lens · {0} | Nota Lens · {0} | LensWindow.cs |
| MIDI mappings | Assignations MIDI | MidiMapView.cs |
| No mappings yet.⏎ ⏎ Click MIDI (top-right), then click any highlighted⏎ control and move a knob or fader on your controller —⏎ or press a button or move a stick on a gamepad. | Aucune assignation pour l’instant.⏎ ⏎ Cliquer sur MIDI (en haut à droite), puis sur une commande⏎ en surbrillance, et bouger un bouton ou un fader du contrôleur —⏎ ou appuyer sur un bouton ou bouger un stick d’une manette. | MidiMapView.cs |
| Generate | Générer | MidiToolsView.cs |
| Transform | Transformer | MidiToolsView.cs |
| Seed | Graine | MidiToolsView.cs |
| Reroll | Retirer au sort | MidiToolsView.cs |
| Apply | Appliquer | MidiToolsView.cs |
| Defaults | Valeurs par défaut | MidiToolsView.cs |
| Revert | Rétablir | MidiToolsView.cs |
| {0} selected | {0} sélectionné(s) | MidiToolsView.cs |
| clip · {0} | clip · {0} | MidiToolsView.cs |
| {0} notes · applied | {0} notes · appliqué | MidiToolsView.cs |
| Pick a tool | Choisir un outil | MidiToolsView.cs |
| {0:+0;−0;±0} notes | {0:+0;−0;±0} notes | MidiToolsView.cs |
| {0} notes, was {1} | {0} notes, au lieu de {1} | MidiToolsView.cs |
| Parameters | Paramètres | MidiToolsView.cs |
| Pick a tool to see its parameters | Choisir un outil pour voir ses paramètres | MidiToolsView.cs |
| Click a clip to edit it · the lock keeps a clip for reference | Clic sur un clip pour l’éditer · le verrou garde un clip comme référence | PianoRollView.ClipStrip.cs |
| Other CC | Autre CC | PianoRollView.Controllers.cs |
| Drag to draw | Glisser pour dessiner | PianoRollView.Controllers.cs |
| Drag to draw a block | Glisser pour dessiner un bloc | PianoRollView.Controllers.cs |
| Clear {0} | Effacer {0} | PianoRollView.Controllers.cs |
| Bend | Bend | PianoRollView.Expression.cs |
| Pres | Pres | PianoRollView.Expression.cs |
| Slide | Slide | PianoRollView.Expression.cs |
| Pressure | Pression | PianoRollView.Expression.cs |
| Clear All Expression | Effacer toute l’expression | PianoRollView.Expression.cs |
| A Session slot plays on its own loop clock — launch it to play from the start | Un emplacement de Session joue sur sa propre horloge de boucle — le lancer pour jouer depuis le début | PianoRollView.cs |
| No selection | Aucune sélection | PianoRollView.cs |
| 1 note · {0} · vel {1} | 1 note · {0} · vél {1} | PianoRollView.cs |
| {0} notes | {0} notes | PianoRollView.cs |

## Outils MIDI (piano roll) (114)

| English | Français | Source |
|---|---|---|
| A step sequencer in scale degrees — pick a contour and a span, get a line. | Un séquenceur pas à pas en degrés de gamme — choisir un contour et une étendue, obtenir une ligne. | MidiGenerators.cs |
| Accent Vel | Vélocité d’accent | MidiGenerators.cs |
| Accents | Accents | MidiGenerators.cs |
| Arch | Arche | MidiGenerators.cs |
| Cluster | Cluster | MidiGenerators.cs |
| Contour | Contour | MidiGenerators.cs |
| Density | Densité | MidiGenerators.cs |
| Drop 2 | Drop 2 | MidiGenerators.cs |
| Euclidean | Euclidien | MidiGenerators.cs |
| Exponential | Exponentielle | MidiGenerators.cs |
| Grows every note into a chord, voiced the way you would play it. | Transforme chaque note en accord, voicé comme on le jouerait. | MidiGenerators.cs |
| Lays down a pattern that leans on the strong beats, as densely as you ask. | Pose un motif qui s’appuie sur les temps forts, aussi dense qu’on le demande. | MidiGenerators.cs |
| Learns the clip's intervals and rhythm, then writes a new take in the same voice. | Apprend les intervalles et le rythme du clip, puis écrit une nouvelle prise dans le même style. | MidiGenerators.cs |
| Logarithmic | Logarithmique | MidiGenerators.cs |
| Melodic Steps | Pas mélodiques | MidiGenerators.cs |
| Pitch Var | Variation de hauteur | MidiGenerators.cs |
| Power | Alimentation | MidiGenerators.cs |
| Pulses | Impulsions | MidiGenerators.cs |
| Random Walk | Marche aléatoire | MidiGenerators.cs |
| Rhythm | Rythme | MidiGenerators.cs |
| Rhythm Var | Variation de rythme | MidiGenerators.cs |
| Root | Fondamentale | MidiGenerators.cs |
| Spread | Spread | MidiGenerators.cs |
| Spreads a number of hits as evenly as a number of steps allows. | Répartit un nombre de frappes aussi régulièrement que le permet un nombre de pas. | MidiGenerators.cs |
| Square | Carré | MidiGenerators.cs |
| Stacks | Piles | MidiGenerators.cs |
| Traces a curve across the clip and drops a note wherever it passes a step. | Trace une courbe sur le clip et dépose une note chaque fois qu’elle croise un pas. | MidiGenerators.cs |
| Valley | Vallée | MidiGenerators.cs |
| Variation | Variation | MidiGenerators.cs |
| Vel Falloff | Atténuation de vél. | MidiGenerators.cs |
| Vel Follows | Vél. suit | MidiGenerators.cs |
| Vel Var | Var. de vél. | MidiGenerators.cs |
| Voices | Voix | MidiGenerators.cs |
| Voicing | Voicing | MidiGenerators.cs |
| Zigzag | Zigzag | MidiGenerators.cs |
| On | On | MidiTool.cs |
| Accent | Accent | MidiTransforms.cs |
| Alternate | Alterner | MidiTransforms.cs |
| Amount | Quantité | MidiTransforms.cs |
| Arpeggiate | Arpéger | MidiTransforms.cs |
| Bends the time axis so a phrase accelerates, drags or breathes. | Déforme l’axe du temps pour qu’une phrase accélère, traîne ou respire. | MidiTransforms.cs |
| Blend | Blend | MidiTransforms.cs |
| Breaks chords into a running sequence. | Découpe les accords en une séquence continue. | MidiTransforms.cs |
| Centre | Centre | MidiTransforms.cs |
| Centre Out | Du centre vers l’extérieur | MidiTransforms.cs |
| Chance | Probabilité | MidiTransforms.cs |
| Chord | Accord | MidiTransforms.cs |
| Chromatic | Chromatique | MidiTransforms.cs |
| Chunk | Segment | MidiTransforms.cs |
| Connect | Relier | MidiTransforms.cs |
| Converge | Converger | MidiTransforms.cs |
| Curve | Courbe | MidiTransforms.cs |
| Cycles | Cycles | MidiTransforms.cs |
| Decorates notes with grace notes, trills and rolls. | Orne les notes de notes d’agrément, de trilles et de roulements. | MidiTransforms.cs |
| Distance | Distance | MidiTransforms.cs |
| Diverge | Diverger | MidiTransforms.cs |
| Down | Bas | MidiTransforms.cs |
| Down-Up | Bas-haut | MidiTransforms.cs |
| Draws a curve through the velocities instead of editing them one by one. | Trace une courbe à travers les vélocités au lieu de les modifier une par une. | MidiTransforms.cs |
| Fill | Remplissage | MidiTransforms.cs |
| Fills the gaps between notes with runs that walk to the next pitch. | Comble les vides entre les notes par des traits qui montent ou descendent vers la hauteur suivante. | MidiTransforms.cs |
| Flam | Flam | MidiTransforms.cs |
| Grace | Appogiature | MidiTransforms.cs |
| Humanize | Humaniser | MidiTransforms.cs |
| In Scale | Dans la gamme | MidiTransforms.cs |
| In scale | Dans la gamme | MidiTransforms.cs |
| Interval | Intervalle | MidiTransforms.cs |
| Keep Ends | Garder les extrémités | MidiTransforms.cs |
| Max Steps | Pas max. | MidiTransforms.cs |
| Mirror Pitch | Hauteur en miroir | MidiTransforms.cs |
| Mordent | Mordant | MidiTransforms.cs |
| Octaves | Octaves | MidiTransforms.cs |
| Ornament | Ornement | MidiTransforms.cs |
| Per Pitch | Par hauteur | MidiTransforms.cs |
| Period | Période | MidiTransforms.cs |
| Pulls notes onto the grid, with swing and a strength short of rigid. | Cale les notes sur la grille, avec du swing et une force qui reste souple. | MidiTransforms.cs |
| Pulls pitch and rhythm apart and puts them back together differently. | Sépare hauteur et rythme puis les recompose autrement. | MidiTransforms.cs |
| Quantize Ends | Quantifier les fins | MidiTransforms.cs |
| Ramp Down | Rampe descendante | MidiTransforms.cs |
| Ramp Up | Rampe ascendante | MidiTransforms.cs |
| Random | Aléatoire | MidiTransforms.cs |
| Recombine | Recombiner | MidiTransforms.cs |
| Reverse Pitch | Hauteur inversée | MidiTransforms.cs |
| Reverse Time | Temps inversé | MidiTransforms.cs |
| Rewrites note lengths — from staccato through legato to overlapping. | Réécrit les durées de notes — du staccato au legato jusqu’au chevauchement. | MidiTransforms.cs |
| Roll | Roulement | MidiTransforms.cs |
| Rolls the notes of a chord out in time instead of hitting them together. | Étale dans le temps les notes d’un accord au lieu de les frapper ensemble. | MidiTransforms.cs |
| Rotate | Rotation | MidiTransforms.cs |
| Rotate Pitch | Rotation de hauteur | MidiTransforms.cs |
| S-Curve | Courbe en S | MidiTransforms.cs |
| Scale Length | Longueur de la gamme | MidiTransforms.cs |
| Shape | Forme | MidiTransforms.cs |
| Shuffle Both | Mélanger les deux | MidiTransforms.cs |
| Shuffle Pitch | Mélanger la hauteur | MidiTransforms.cs |
| Shuffle Rhythm | Mélanger le rythme | MidiTransforms.cs |
| Snap to Grid | Magnétisme sur la grille | MidiTransforms.cs |
| Span | Étendue | MidiTransforms.cs |
| Steps | Pas | MidiTransforms.cs |
| Strum | Strum | MidiTransforms.cs |
| Style | Style | MidiTransforms.cs |
| Swing | Swing | MidiTransforms.cs |
| Tension | Tension | MidiTransforms.cs |
| Time Warp | Déformation temporelle | MidiTransforms.cs |
| To Next Note | Jusqu’à la note suivante | MidiTransforms.cs |
| Triangle | Triangle | MidiTransforms.cs |
| Trill | Trille | MidiTransforms.cs |
| Turn | Gruppetto | MidiTransforms.cs |
| Type | Type | MidiTransforms.cs |
| Up | Haut | MidiTransforms.cs |
| Up-Down | Haut-bas | MidiTransforms.cs |
| Vel Decay | Déclin de vél. | MidiTransforms.cs |
| Vel Slope | Pente de vél. | MidiTransforms.cs |
| Velocity Shaper | Façonneur de vélocité | MidiTransforms.cs |
| Warp Lengths | Longueurs de warp | MidiTransforms.cs |

## Appareils (cartes et fenêtres) (1283)

| English | Français | Source |
|---|---|---|
| Cutoff over time | Coupure au fil du temps | AutoFilterViews.cs |
| out of scale | hors gamme | AutoShiftViews.cs |
| Captured sound · repeating | Son capturé · répétition | BeatRepeatViews.cs |
| Repeat filter · off | Repeat filter · off | BeatRepeatViews.cs |
| Capture and repeat | Capturer et répéter | BeatRepeatViews.cs |
| Level and reduction | Niveau et réduction | CeilingViews.cs |
| no key signal | aucun signal de clé | CompViews.cs |
| Pitch · arp (step types shape the rhythm) | Hauteur · arp (les types de pas façonnent le rythme) | ConsortViews.cs |
| Pitch · step offsets from the key | Hauteur · décalages des pas par rapport à la touche | ConsortViews.cs |
| level step shown enlarged | pas de niveau affiché agrandi | CrushViews.cs |
| Duck — acts above the threshold | Duck — agit au-dessus du seuil | DynamicEqCurve.cs |
| Lift — acts below the threshold | Lift — agit sous le seuil | DynamicEqCurve.cs |
| Band on | Bande active | Eq8Views.cs |
| no source assigned | aucune source assignée | FluxReactScope.cs |
| String · 2 pickups | Corde · 2 micros | KeysViews.cs |
| Clear the peak hold | Effacer le maintien de crête | MeterBar.cs |
| Not available yet | Pas encore disponible | NaBadge.cs |
| drag an operator onto another to re-route | glisser un opérateur sur un autre pour le reconnecter | OperatorAlgoViz.cs |
| bar · quantize {0} | mesure · quantification {0} | PendulumViews.cs |
| Tail held · no decay | Queue maintenue · sans déclin | ReverbViews.cs |
| Click: step on / off · Shift-click: accent · Alt-click: quiet step⏎ Drag across to paint · drag up / down or scroll to set the velocity · right-click for more | Clic : pas actif / inactif · Maj-clic : accent · Alt-clic : pas faible⏎ Glisser pour peindre · glisser vers le haut / bas ou défiler pour régler la vélocité · clic droit pour plus d’options | RhythmViews.cs |
| Detector · full band | Détecteur · pleine bande | ShutterViews.cs |
| Gate envelope · trigger | Gate envelope · trigger | ShutterViews.cs |
| Signal and duck | Signal et duck | ShutterViews.cs |
| Signal and gate | Signal et gate | ShutterViews.cs |
| {0} · {1} cycles · unison {2} | {0} · {1} cycles · unisson {2} | SynthViz.cs |
| lows at the bottom · highs at the top | graves en bas · aigus en haut | UtilityViews.cs |
| Input and output | Entrée et sortie | UtilityViews.cs |
| Width by frequency | Largeur par fréquence | UtilityViews.cs |
| Type value… | Saisir une valeur… | ValueEntry.cs |
| C D EF G A B | C D EF G A B | ValueText.cs |
| Pick a carrier track · until then the sound passes through | Choisir une piste porteuse · d’ici là le son passe tel quel | VocoderGraph.cs |
| Devices / Clip | Appareils / Clip | DetailWindow.cs |
| Changed since you loaded {0} · choose a preset | Modifié depuis le chargement de {0} · choisir un préréglage | DeviceCardKit.cs |
| Choose a preset | Choisir un préréglage | DeviceCardKit.cs |
| Init | Init | DeviceCardKit.cs |
| Next preset | Préréglage suivant | DeviceCardKit.cs |
| Previous preset | Préréglage précédent | DeviceCardKit.cs |
| Value | Valeur | DeviceCardKit.cs |
| Analog — a gentle saturation after the filter, like an analog circuit | Analog — une légère saturation après le filtre, comme un circuit analogique | AutoFilterDeviceBody.cs |
| Cutoff — the base the modulation moves from; the dashed mark is where it is now. Double-click resets, ⌥-click types a value. | Cutoff — la base d’où part la modulation ; le repère en pointillés indique la valeur actuelle. Double-clic pour réinitialiser, ⌥-clic pour saisir une valeur. | AutoFilterDeviceBody.cs |
| Drag sideways for the cutoff, up and down for the resonance. The teal band is how far the envelope and LFO can move the cutoff; the dashed curve is where they have it now. | Glisser latéralement pour la fréquence de coupure, vers le haut et le bas pour la résonance. La bande turquoise montre jusqu’où l’enveloppe et le LFO peuvent déplacer la coupure ; la courbe en pointillés indique où elle se trouve maintenant. | AutoFilterDeviceBody.cs |
| Envelope follower on / off | Suiveur d’enveloppe activé / désactivé | AutoFilterDeviceBody.cs |
| Hold — keep the envelope at its peak for the HOLD time before it releases (∞ freezes it) | Hold — garde l’enveloppe à son pic pendant le temps HOLD avant le relâchement (∞ la fige) | AutoFilterDeviceBody.cs |
| LFO on / off | LFO activé / désactivé | AutoFilterDeviceBody.cs |
| None — this track's input | Aucun — entrée de cette piste | AutoFilterDeviceBody.cs |
| Resonance — the peak at the cutoff; the dashed mark is where it is now. Double-click resets, ⌥-click types a value. | Resonance — le pic à la coupure ; le repère en pointillés indique la valeur actuelle. Double-clic pour réinitialiser, ⌥-clic pour saisir une valeur. | AutoFilterDeviceBody.cs |
| Retrig — restart the LFO on each input onset (−24 dBFS) and when play starts | Retrig — relance le LFO à chaque attaque d’entrée (−24 dBFS) et au démarrage de la lecture | AutoFilterDeviceBody.cs |
| SIDECHAIN KEY — ENVELOPE SOURCE | Clé sidechain — source de l’enveloppe | AutoFilterDeviceBody.cs |
| Sidechain key — the envelope follows another track instead of this one. Switch on to pick the source and the key gain; right-click to change it. | Clé sidechain — l’enveloppe suit une autre piste au lieu de celle-ci. L’activer pour choisir la source et le gain de clé ; clic droit pour la modifier. | AutoFilterDeviceBody.cs |
| Stereo phase — the right channel's LFO runs ahead of the left (90° when switched on; right-click steps 45° … 180°) | Stereo phase — le LFO du canal droit avance sur celui du gauche (90° une fois activé ; clic droit par pas de 45° … 180°) | AutoFilterDeviceBody.cs |
| The LFO's movement over the window — raw shape dashed, what the filter follows (after Smooth) in neutral ink, the right channel in rose when stereo is on. Drag up or down for the amount, sideways for the start phase. | Le mouvement du LFO sur la fenêtre — forme brute en pointillés, ce que suit le filtre (après Smooth) en neutre, le canal droit en rose quand la stéréo est activée. Glisser vers le haut ou le bas pour la quantité, latéralement pour la phase de départ. | AutoFilterDeviceBody.cs |
| The input's envelope (teal) and the cutoff it drives (neutral ink), the base cutoff dashed. Drag up or down for the amount. | L’enveloppe de l’entrée (turquoise) et la coupure qu’elle pilote (neutre), la coupure de base en pointillés. Glisser vers le haut ou le bas pour la quantité. | AutoFilterDeviceBody.cs |
| LFO not assigned | LFO non assigné | AutoFilterDeviceBody.cs |
| envelope not assigned | enveloppe non assignée | AutoFilterDeviceBody.cs |
| no modulation assigned | aucune modulation assignée | AutoFilterDeviceBody.cs |
| Auto rides the gain to the target over Window; Manual holds the fader where you put it (Match sets it to the distance to the target) | Auto amène le gain vers la cible sur Window ; Manual garde le fader là où il est placé (Match le règle sur l’écart à la cible) | AutoGainDeviceBody.cs |
| Ceiling, −6 … 0 dBTP — drag up / down (Shift for fine steps), ⌥-click to type, double-click resets to −1.0 | Ceiling, −6 … 0 dBTP — glisser vers le haut / bas (Maj pour les pas fins), ⌥-clic pour saisir, double-clic pour revenir à −1,0 | AutoGainDeviceBody.cs |
| Fixed target — no reference | Cible fixe — pas de référence | AutoGainDeviceBody.cs |
| Input loudness on the chosen scale (LUFS); the teal mark is the target | Volume d’entrée sur l’échelle choisie (LUFS) ; le repère turquoise est la cible | AutoGainDeviceBody.cs |
| Input silent: the measurement and the gain hold | Entrée silencieuse : la mesure et le gain sont maintenus | AutoGainDeviceBody.cs |
| Leveler bypassed: the output equals the input | Leveler contourné : la sortie est égale à l’entrée | AutoGainDeviceBody.cs |
| Match — set the manual gain to the distance between the measured input and the target | Match — règle le gain manuel sur l’écart entre l’entrée mesurée et la cible | AutoGainDeviceBody.cs |
| Max gain — the most the correction may boost or cut, 0 … 24 dB | Max gain — le renfort ou l’atténuation maximum de la correction, 0 … 24 dB | AutoGainDeviceBody.cs |
| Output loudness on the chosen scale (LUFS); the teal mark is the target | Volume de sortie sur l’échelle choisie (LUFS) ; le repère turquoise est la cible | AutoGainDeviceBody.cs |
| Output true peak (dBTP); the teal mark is the ceiling while True-peak safe is on | True peak de sortie (dBTP) ; le repère turquoise est le plafond tant que True-peak safe est activé | AutoGainDeviceBody.cs |
| Output {0} LU from the target · Match sets {1} dB | Sortie à {0} LU de la cible · Match règle {1} dB | AutoGainDeviceBody.cs |
| Reset — restart the measurement from the loudness now, so the ride catches up at once | Reset — relance la mesure à partir du volume actuel, pour que l’ajustement rattrape aussitôt | AutoGainDeviceBody.cs |
| Response — Slow glides over the whole Window with a 20 ms look-ahead; Fast over a quarter of it with 5 ms | Response — Slow glisse sur toute la fenêtre Window avec 20 ms d’anticipation ; Fast sur un quart de celle-ci avec 5 ms | AutoGainDeviceBody.cs |
| Scale — what the correction reads: Momentary (400 ms, fast and nervous), Short-term (3 s) or Integrated (12 s with BS.1770 gating, the steadiest) | Scale — ce que lit la correction : Momentary (400 ms, rapide et nerveux), Short-term (3 s) ou Integrated (12 s avec gating BS.1770, le plus stable) | AutoGainDeviceBody.cs |
| Target loudness, −36 … 0 LUFS. Click the value for the standards or to match another track's loudness (a reference); drag the dashed line in the graph for the same | Volume cible, −36 … 0 LUFS. Cliquer sur la valeur pour les standards ou pour s’aligner sur le volume d’une autre piste (une référence) ; glisser la ligne en pointillés du graphique pour la même chose | AutoGainDeviceBody.cs |
| The correction applied, ±Max Gain around 0 dB (trim not included) | La correction appliquée, ±Max Gain autour de 0 dB (trim non inclus) | AutoGainDeviceBody.cs |
| The correction around 0 dB, ±Max Gain. In Manual drag it (Shift for fine steps); ⌥-click types, double-click resets to 0 dB. In Auto it shows the ride | La correction autour de 0 dB, ±Max Gain. En Manual, la glisser (Maj pour les pas fins) ; ⌥-clic pour saisir, double-clic pour revenir à 0 dB. En Auto, elle montre l’ajustement | AutoGainDeviceBody.cs |
| The last 8 s: input, output and the target with its ±1 LU band (momentary loudness). Drag up / down to move the target; ⌥-click types, double-click resets it | Les 8 dernières secondes : entrée, sortie et cible avec sa bande de ±1 LU (volume momentané). Glisser vers le haut / bas pour déplacer la cible ; ⌥-clic pour saisir, double-clic pour la réinitialiser | AutoGainDeviceBody.cs |
| Trim — a fixed offset after the correction, −12 … +12 dB (the output then sits that far off the target) | Trim — un décalage fixe après la correction, −12 … +12 dB (la sortie s’écarte alors d’autant de la cible) | AutoGainDeviceBody.cs |
| True-peak safe — a look-ahead limiter keeps the output under the ceiling (adds 5 / 20 ms of latency, compensated) | True-peak safe — un limiteur à anticipation maintient la sortie sous le plafond (ajoute 5 / 20 ms de latence, compensée) | AutoGainDeviceBody.cs |
| Waiting for signal — the gain holds until the input has been measured | En attente de signal — le gain est maintenu jusqu’à la mesure de l’entrée | AutoGainDeviceBody.cs |
| What the leveler is doing now | Ce que fait le leveler en ce moment | AutoGainDeviceBody.cs |
| Window — how long the ride takes to reach the target, 0.4 … 10 s (Fast: a quarter of it) | Window — le temps que met l’ajustement à atteindre la cible, 0,4 … 10 s (Fast : un quart de ce temps) | AutoGainDeviceBody.cs |
| limited by max cut | limité par la coupe max. | AutoGainDeviceBody.cs |
| limited by max gain | limité par le gain max. | AutoGainDeviceBody.cs |
| limited by true-peak | limité par le true-peak | AutoGainDeviceBody.cs |
| waiting for signal | en attente de signal | AutoGainDeviceBody.cs |
| Above 8 Hz the movement reads as a flutter, not as panning | Au-delà de 8 Hz, le mouvement s’entend comme un flottement et non comme un panoramique | AutoPanDeviceBody.cs |
| Amount or Mix at zero: nothing moves | Amount ou Mix à zéro : rien ne bouge | AutoPanDeviceBody.cs |
| Amount — the depth: each channel dips to 1 − Amount at the LFO's low point | Amount — la profondeur : chaque canal descend à 1 − Amount au creux du LFO | AutoPanDeviceBody.cs |
| Both channels' gain over two LFO cycles — L neutral, R teal, Phase later — with the floor the depth reaches. Drag up / down for Amount, left / right for Phase; ⌥-click types, double-click resets both. | Gain des deux canaux sur deux cycles du LFO — L neutre, R turquoise, Phase plus tard — avec le plancher atteint par la profondeur. Glisser vers le haut / bas pour Amount, à gauche / droite pour Phase ; ⌥-clic pour saisir, double-clic pour réinitialiser les deux. | AutoPanDeviceBody.cs |
| GAIN · 2 CYCLES | Gain · 2 cycles | AutoPanDeviceBody.cs |
| Mix — the moving signal against the dry one | Mix — le signal en mouvement face au signal sec | AutoPanDeviceBody.cs |
| Panner bypassed: the signal passes unchanged | Panner contourné : le signal passe inchangé | AutoPanDeviceBody.cs |
| Phase 0°: L and R move together, the level dips to  | Phase 0° : L et R bougent ensemble, le niveau descend à  | AutoPanDeviceBody.cs |
| Phase — the right LFO against the left: 0° tremolo, 90° a circling pan, 180° auto-pan | Phase — le LFO droit par rapport au gauche : 0° trémolo, 90° panoramique circulaire, 180° auto-pan | AutoPanDeviceBody.cs |
| Rate — the free LFO rate, 0.01 … 40 Hz | Rate — la vitesse libre du LFO, 0,01 … 40 Hz | AutoPanDeviceBody.cs |
| Rate — the note value of one LFO cycle, 4/1 … 1/32 with dotted (D) and triplet (T) values | Rate — la valeur de note d’un cycle de LFO, 4/1 … 1/32 avec valeurs pointées (D) et en triolets (T) | AutoPanDeviceBody.cs |
| Shape — sharpens Sine, Tri and Saw toward a square; on S&H it is Glide, the slide from one step to the next | Shape — durcit Sine, Tri et Saw vers un carré ; sur S&H c’est Glide, le glissement d’un pas au suivant | AutoPanDeviceBody.cs |
| The LFO shape. S&H draws a random step every cycle; the right channel plays the same steps Phase later | La forme du LFO. S&H tire un pas aléatoire à chaque cycle ; le canal droit joue les mêmes pas avec un décalage Phase | AutoPanDeviceBody.cs |
| The channel's output peak, −60 … 0 dBFS, and the gain the LFO gives it now, dB | La crête de sortie du canal, −60 … 0 dBFS, et le gain que le LFO lui donne en ce moment, dB | AutoPanDeviceBody.cs |
| What the phase makes: tremolo near 0°, auto-pan near 180°, an offset pan between | Effet de la phase : trémolo près de 0°, auto-pan près de 180°, un panoramique décalé entre les deux | AutoPanDeviceBody.cs |
| Where the sound sits now between left and right, over the swing the LFO covers | Où se trouve le son entre gauche et droite en ce moment, sur l’excursion couverte par le LFO | AutoPanDeviceBody.cs |
| 0° — tremolo: both channels together | 0° — trémolo : les deux canaux ensemble | AutoPanDeviceBody.cs |
| 90° — a quarter cycle apart: the sound circles | 90° — un quart de cycle d’écart : le son tourne en cercle | AutoPanDeviceBody.cs |
| 180° — auto-pan: one side up while the other is down | 180° — auto-pan : un côté monte quand l’autre descend | AutoPanDeviceBody.cs |
| Amount — how far the voice is pulled to the note | Amount — l’attraction de la voix vers la note | AutoShiftDeviceBody.cs |
| Amount — how far the voice is pulled to the target note | Amount — l’attraction de la voix vers la note cible | AutoShiftDeviceBody.cs |
| Bend — the widest move between notes that bends over Glide; wider leaps jump | Bend — le plus grand écart entre notes qui glisse via Glide ; les sauts plus larges sont directs | AutoShiftDeviceBody.cs |
| Custom — edit on the Scale tab | Custom — à modifier dans l’onglet Scale | AutoShiftDeviceBody.cs |
| Follow — copy the key and scale of a Nota Scale MIDI effect | Follow — copie la tonalité et la gamme d’un effet MIDI Nota Scale | AutoShiftDeviceBody.cs |
| Follow — copy the key and scale of a Nota Scale MIDI effect (this track, the MIDI source, or the first one found) | Follow — copie la tonalité et la gamme d’un effet MIDI Nota Scale (celui de cette piste, de la source MIDI, ou le premier trouvé) | AutoShiftDeviceBody.cs |
| From — the instrument track whose notes set the target (its MIDI clips and effects, even when muted) | From — la piste d’instrument dont les notes définissent la cible (ses clips et effets MIDI, même muette) | AutoShiftDeviceBody.cs |
| Glide — how long the target bends from one note to the next (Pitch bend from MIDI) | Glide — la durée du glissement de la cible d’une note à la suivante (Pitch bend from MIDI) | AutoShiftDeviceBody.cs |
| How long each note was sung (the last ~16 bars) over the scale's notes — neutral in the scale, teal outside it. Click a column to add or remove its note | La durée de chaque note chantée (les ~16 dernières mesures) sur les notes de la gamme — neutre dans la gamme, turquoise en dehors. Cliquer une colonne pour ajouter ou retirer sa note | AutoShiftDeviceBody.cs |
| Key Source is not MIDI | Key Source n’est pas MIDI | AutoShiftDeviceBody.cs |
| Key source — Auto follows the key you sing, Manual uses the key and scale you set, MIDI takes the target from another track's notes (MIDI tab) | Key source — Auto suit la tonalité chantée, Manual utilise la tonalité et la gamme réglées, MIDI prend la cible dans les notes d’une autre piste (onglet MIDI) | AutoShiftDeviceBody.cs |
| Key — the tonic; the scale's notes are counted from it | Key — la tonique ; les notes de la gamme sont comptées à partir d’elle | AutoShiftDeviceBody.cs |
| Learn — listen to what is sung, then click again to set the key and scale that fit it best | Learn — écoute ce qui est chanté, puis cliquer à nouveau pour régler la tonalité et la gamme qui conviennent le mieux | AutoShiftDeviceBody.cs |
| Note — the voice goes to the last held note; Scale — the held notes form the scale | Note — la voix va à la dernière note tenue ; Scale — les notes tenues forment la gamme | AutoShiftDeviceBody.cs |
| Pitch bend from MIDI | Pitch bend depuis le MIDI | AutoShiftDeviceBody.cs |
| Range — the largest correction; a note further off than this is only pulled this far | Range — la plus grande correction ; une note plus éloignée n’est ramenée que de cette valeur | AutoShiftDeviceBody.cs |
| Scale — Chromatic, Major, Minor, Pentatonic (major or minor — click again to swap) or Custom (the notes below) | Scale — Chromatic, Major, Minor, Pentatonic (majeure ou mineure — cliquer à nouveau pour alterner) ou Custom (les notes ci-dessous) | AutoShiftDeviceBody.cs |
| Scale — the notes the voice is pulled to | Scale — les notes vers lesquelles la voix est attirée | AutoShiftDeviceBody.cs |
| Skip sibilants — s, sh, t and breath pass unshifted, so they don't turn metallic | Skip sibilants — s, ch, t et le souffle passent sans transposition, pour ne pas devenir métalliques | AutoShiftDeviceBody.cs |
| Source — sets the detection range for a voice type or an instrument | Source — définit la plage de détection pour un type de voix ou un instrument | AutoShiftDeviceBody.cs |
| Speed — how fast the voice follows the target | Speed — la vitesse à laquelle la voix suit la cible | AutoShiftDeviceBody.cs |
| The correction now — up or down from the centre, full height = 2 semitones | La correction actuelle — vers le haut ou le bas depuis le centre, pleine hauteur = 2 demi-tons | AutoShiftDeviceBody.cs |
| The detected pitch (teal dots) and the corrected output (neutral ink) over the last two seconds, on the scale's note lanes; the target lane is lit | La hauteur détectée (points turquoise) et la sortie corrigée (neutre) sur les deux dernières secondes, sur les voies de notes de la gamme ; la voie cible est allumée | AutoShiftDeviceBody.cs |
| The detected pitch, low to high across the detection range (Detect tab) | La hauteur détectée, du grave à l’aigu sur la plage de détection (onglet Detect) | AutoShiftDeviceBody.cs |
| The key that fits what was sung (Krumhansl profiles over the last ~16 bars), and the runner-up. Learn sets it; Auto follows it | La tonalité qui correspond à ce qui a été chanté (profils de Krumhansl sur les ~16 dernières mesures), et la seconde. Learn la règle ; Auto la suit | AutoShiftDeviceBody.cs |
| The source track's notes (neutral blocks, accent while sounding) and the voice (teal) over the last two seconds — with Key Source MIDI the voice is pulled to the notes | Les notes de la piste source (blocs neutres, accent pendant qu’elles sonnent) et la voix (turquoise) sur les deux dernières secondes — avec Key Source MIDI, la voix est attirée vers les notes | AutoShiftDeviceBody.cs |
| What the detector hears now and the correction applied — or, with Key Source MIDI, the note the voice is pulled to | Ce que le détecteur entend en ce moment et la correction appliquée — ou, avec Key Source MIDI, la note vers laquelle la voix est attirée | AutoShiftDeviceBody.cs |
| latch holds the target between notes | latch maintient la cible entre les notes | AutoShiftDeviceBody.cs |
| no correction between notes | aucune correction entre les notes | AutoShiftDeviceBody.cs |
| pick a track in FROM | choisir une piste dans FROM | AutoShiftDeviceBody.cs |
| sing a few bars | chanter quelques mesures | AutoShiftDeviceBody.cs |
| Follow Scale device | Suivre l’appareil Scale | AutoShiftDeviceBody.cs |
| no note held | aucune note tenue | AutoShiftDeviceBody.cs |
| Human — keeps vibrato and slow drift around the corrected note (0 = flat) | Human — garde le vibrato et la dérive lente autour de la note corrigée (0 = plat) | AutoShiftDeviceBody.cs |
| Latch — keep the last target between notes (off = no correction while no note is held) | Latch — garde la dernière cible entre les notes (off = aucune correction tant qu’aucune note n’est tenue) | AutoShiftDeviceBody.cs |
| Oct lock — the note's own octave (off = whichever octave of it is nearest the voice) | Oct lock — l’octave propre de la note (off = l’octave de la note la plus proche de la voix) | AutoShiftDeviceBody.cs |
| Pitch bend from MIDI — moves between notes bend over Glide when they are within Bend; off = the target steps | Pitch bend from MIDI — les passages d’une note à l’autre glissent via Glide s’ils restent dans Bend ; off = la cible saute | AutoShiftDeviceBody.cs |
| Preserve formants — the vowel colour stays put while the pitch moves (off = it moves with the pitch, like a tape speed-up) | Preserve formants — la couleur des voyelles reste en place quand la hauteur bouge (off = elle suit la hauteur, comme une bande accélérée) | AutoShiftDeviceBody.cs |
| Speed — how fast the retune glides (2 ms = hard tune) | Speed — la vitesse du glissement de la correction (2 ms = hard tune) | AutoShiftDeviceBody.cs |
| Chance — how likely each interval fires a repeat | Chance — la probabilité que chaque intervalle déclenche une répétition | BeatRepeatDeviceBody.cs |
| Filter — Off, or a low-pass, band-pass or high-pass on the repeats | Filter — Off, ou un passe-bas, passe-bande ou passe-haut sur les répétitions | BeatRepeatDeviceBody.cs |
| Freq — the filter's frequency | Freq — la fréquence du filtre | BeatRepeatDeviceBody.cs |
| Gate — how long the burst lasts, in 16ths of the interval | Gate — la durée de la rafale, en seizièmes de l’intervalle | BeatRepeatDeviceBody.cs |
| Grid — the length of the slice that repeats | Grid — la durée de la tranche qui se répète | BeatRepeatDeviceBody.cs |
| Interval — how often it rolls Chance | Interval — la fréquence à laquelle Chance est tiré | BeatRepeatDeviceBody.cs |
| Interval — how often it rolls Chance: 1/8, 1/4, 1/2 note, or every 1, 2, 4 bars | Interval — la fréquence à laquelle Chance est tiré : 1/8, 1/4, 1/2 note, ou toutes les 1, 2, 4 mesures | BeatRepeatDeviceBody.cs |
| Latch — the Repeat button stays on after a click instead of only while held | Latch — le bouton Repeat reste actif après un clic au lieu de l’être seulement tant qu’il est tenu | BeatRepeatDeviceBody.cs |
| Mode — Mix: the repeats play over the dry; Insert: they replace it while they play; Gate: only the burst sounds | Mode — Mix : les répétitions jouent par-dessus le signal sec ; Insert : elles le remplacent pendant qu’elles jouent ; Gate : seule la rafale sonne | BeatRepeatDeviceBody.cs |
| Narrow with repeats — each repeat narrows the band and moves it with the pitch | Narrow with repeats — chaque répétition rétrécit la bande et la déplace avec la hauteur | BeatRepeatDeviceBody.cs |
| Offset — where in the interval it fires, in 16ths | Offset — à quel endroit de l’intervalle il se déclenche, en seizièmes | BeatRepeatDeviceBody.cs |
| Pitch decay — each repeat drops lower, up to 6 semitones a repeat | Pitch decay — chaque répétition descend plus bas, jusqu’à 6 demi-tons par répétition | BeatRepeatDeviceBody.cs |
| Pitch — the repeats transposed, ±12 semitones | Pitch — les répétitions transposées, ±12 demi-tons | BeatRepeatDeviceBody.cs |
| Repeat filter — the repeats go through the filter (Filter tab) | Repeat filter — les répétitions passent par le filtre (onglet Filter) | BeatRepeatDeviceBody.cs |
| Repeat — repeat the current slice now, for as long as it is held (with Latch: click on, click off) | Repeat — répète la tranche actuelle maintenant, tant qu’il est tenu (avec Latch : un clic pour activer, un clic pour arrêter) | BeatRepeatDeviceBody.cs |
| Reset — stop the repeat and clear the timeline and the meters | Reset — arrête la répétition et efface la ligne de temps et les vumètres | BeatRepeatDeviceBody.cs |
| The last interval's sound (teal) on the grid: the gate from the offset is shaded and the captured slice is brass. Drag the offset line or the gate's end sideways; ⌥-click to type, double-click resets. | Le son du dernier intervalle (turquoise) sur la grille : le gate à partir de l’offset est ombré et la tranche capturée est en laiton. Glisser latéralement la ligne d’offset ou la fin du gate ; ⌥-clic pour saisir, double-clic pour réinitialiser. | BeatRepeatDeviceBody.cs |
| The repeat filter — only the repeats go through it. Solid: the first repeat; teal dashed: a later one when the band narrows and follows the pitch. Drag the node for the frequency, a band edge for the width; ⌥-click to type, double-click resets. | Le filtre des répétitions — seules les répétitions le traversent. Plein : la première répétition ; turquoise en pointillés : une suivante quand la bande se resserre et suit la hauteur. Glisser le nœud pour la fréquence, un bord de bande pour la largeur ; ⌥-clic pour saisir, double-clic pour réinitialiser. | BeatRepeatDeviceBody.cs |
| The repeats' level — after pitch, filter, decay and volume | Le niveau des répétitions — après hauteur, filtre, déclin et volume | BeatRepeatDeviceBody.cs |
| Triplet — the grid in triplets (a third shorter) | Triplet — la grille en triolets (un tiers plus court) | BeatRepeatDeviceBody.cs |
| Two intervals on the grid: each step is the input's level — dim when dry, full where the slice is captured, dimmer with every repeat as it decays. Ticks at the top mark where the interval fires. | Deux intervalles sur la grille : chaque pas est le niveau de l’entrée — pâle quand il est sec, plein là où la tranche est capturée, plus pâle à chaque répétition au fil du déclin. Les repères en haut marquent où l’intervalle se déclenche. | BeatRepeatDeviceBody.cs |
| Variation — the grid floats up to this many steps (double / half) on each trigger | Variation — la grille varie jusqu’à ce nombre de pas (double / moitié) à chaque déclenchement | BeatRepeatDeviceBody.cs |
| What the repeater is doing now — waiting for the interval, capturing the slice, or repeating it — and where the transport is | Ce que fait le répéteur en ce moment — attendre l’intervalle, capturer la tranche, ou la répéter — et où en est le transport | BeatRepeatDeviceBody.cs |
| Width — the band in octaves (low- and high-pass: less is more resonant) | Width — la largeur de la bande en octaves (passe-bas et passe-haut : moins, c’est plus résonant) | BeatRepeatDeviceBody.cs |
| each repeat lower and narrower | chaque répétition plus basse et plus étroite | BeatRepeatDeviceBody.cs |
| each repeat lower, same band | chaque répétition plus basse, même bande | BeatRepeatDeviceBody.cs |
| the grid holds still | la grille reste immobile | BeatRepeatDeviceBody.cs |
| the repeats pass unfiltered | les répétitions passent sans filtre | BeatRepeatDeviceBody.cs |
| the same on every repeat | identique à chaque répétition | BeatRepeatDeviceBody.cs |
| Narrow with repeats | Narrow with repeats | BeatRepeatDeviceBody.cs |
| The input level | Le niveau d’entrée | BeatRepeatDeviceBody.cs |
| Auto release — the release slows as the reduction deepens (up to 5× at 7 dB), so dense passages don't pump | Auto release — le relâchement ralentit quand la réduction s’accentue (jusqu’à 5× à 7 dB), pour que les passages denses ne pompent pas | CeilingDeviceBody.cs |
| Ceiling — the output never goes above it (in dBTP with True peak on) | Ceiling — la sortie ne le dépasse jamais (en dBTP avec True peak activé) | CeilingDeviceBody.cs |
| Character — Clean: transparent, the fastest attack; Punch: a slower attack lets the transient through to the clip; Glue: a slow release and a soft knee under the ceiling | Character — Clean : transparent, l’attaque la plus rapide ; Punch : une attaque plus lente laisse passer le transitoire jusqu’à l’écrêtage ; Glue : un relâchement lent et un genou doux sous le plafond | CeilingDeviceBody.cs |
| Delta · hear what it removes | Delta · entendre ce qui est retiré | CeilingDeviceBody.cs |
| Delta — play the difference between the input and the output: what the limiter takes away | Delta — jouer la différence entre l’entrée et la sortie : ce que retire le limiteur | CeilingDeviceBody.cs |
| Gain reduction, 0 … 24 dB | Réduction de gain, 0 … 24 dB | CeilingDeviceBody.cs |
| Gain — how hard the signal is driven into the ceiling | Gain — la force avec laquelle le signal est poussé vers le plafond | CeilingDeviceBody.cs |
| Integrated loudness since the last Reset, gated (BS.1770), against the Target | Volume intégré depuis le dernier Reset, avec gate (BS.1770), par rapport à Target | CeilingDeviceBody.cs |
| Internal — this track | Interne — cette piste | CeilingDeviceBody.cs |
| Key source — Internal limits on this track's own peaks; pick another track to duck this one when that one peaks | Key source — Internal limite selon les crêtes de cette piste ; choisir une autre piste pour ducker celle-ci quand l’autre atteint son pic | CeilingDeviceBody.cs |
| Momentary (teal), short-term (neutral ink) and integrated (rose) loudness over the last 60 s around the target, ±1 LU shaded. Drag the target line; ⌥-click to type, double-click resets. | Volume momentané (turquoise), à court terme (neutre) et intégré (rose) sur les 60 dernières secondes autour de la cible, ±1 LU ombré. Glisser la ligne de cible ; ⌥-clic pour saisir, double-clic pour réinitialiser. | CeilingDeviceBody.cs |
| Release — how fast the gain comes back (the character scales it; Auto rel lets the signal decide) | Release — la vitesse de retour du gain (le caractère la module ; Auto rel laisse le signal décider) | CeilingDeviceBody.cs |
| Reset peaks — clear the peak holds, the max reduction and the clip count | Reset peaks — efface les maintiens de crête, la réduction maximale et le compte d’écrêtages | CeilingDeviceBody.cs |
| Reset — start the integrated loudness, the range and the peaks again | Reset — relance le volume intégré, la plage et les crêtes | CeilingDeviceBody.cs |
| Target — the loudness the meters compare against: −14 for streaming, −16 podcasts, −23 broadcast. It does not change the sound; drag the dashed line for any value | Target — le volume auquel les vumètres se comparent : −14 pour le streaming, −16 pour les podcasts, −23 pour la diffusion. Cela ne change pas le son ; glisser la ligne en pointillés pour toute valeur | CeilingDeviceBody.cs |
| The gain reduction over the last 4 s: teal where a transient got through to the clip, the dashed line is the mean | La réduction de gain sur les 4 dernières secondes : turquoise là où un transitoire est passé jusqu’à l’écrêtage, la ligne en pointillés est la moyenne | CeilingDeviceBody.cs |
| The input after Gain, with the ceiling marked | L’entrée après Gain, avec le plafond marqué | CeilingDeviceBody.cs |
| The last 4 s: the input (teal), the part over the ceiling (red) and the output (neutral ink), with the reduction under it. Drag the ceiling line; ⌥-click to type, double-click resets. | Les 4 dernières secondes : l’entrée (turquoise), la partie au-dessus du plafond (rouge) et la sortie (neutre), avec la réduction en dessous. Glisser la ligne du plafond ; ⌥-clic pour saisir, double-clic pour réinitialiser. | CeilingDeviceBody.cs |
| True peak — the detector reads the inter-sample peak (4×), so the ceiling holds in dBTP after conversion | True peak — le détecteur lit la crête inter-échantillons (4×), pour que le plafond tienne en dBTP après conversion | CeilingDeviceBody.cs |
| What the limiter listens to — or, with Delta on, that you are hearing only what it takes away | Ce qu’écoute le limiteur — ou, avec Delta activé, que l’on n’entend que ce qu’il retire | CeilingDeviceBody.cs |
| hearing what it removes | écoute de ce qui est retiré | CeilingDeviceBody.cs |
| lets the attack through | laisse passer l’attaque | CeilingDeviceBody.cs |
| the difference in − out | la différence entrée − sortie | CeilingDeviceBody.cs |
| true peak · 4× | true peak · 4× | CeilingDeviceBody.cs |
| slow and dense | lent et dense | CeilingDeviceBody.cs |
| Algorithm share of the blend (drags Blend) | Part de l’algorithme dans le mélange (entraîne Blend) | ChamberDeviceBody.cs |
| Blend — convolution (slate, top) ⇄ algorithm (neutral, bottom). Double-click: 50 / 50 | Blend — convolution (ardoise, haut) ⇄ algorithme (neutre, bas). Double-clic : 50 / 50 | ChamberDeviceBody.cs |
| Concert Hall · Wide | Concert Hall · Wide | ChamberDeviceBody.cs |
| Convolution share of the blend (drags Blend) | Part de la convolution dans le mélange (entraîne Blend) | ChamberDeviceBody.cs |
| Drag the trim lines to trim the IR (Start · Decay); ⌥-click to type, double-click resets. Drop an audio file here to load it. | Glisser les lignes de découpe pour recadrer la réponse impulsionnelle (Start · Decay) ; ⌥-clic pour saisir, double-clic pour réinitialiser. Déposer un fichier audio ici pour le charger. | ChamberDeviceBody.cs |
| Drop an impulse response here (WAV / FLAC / MP3) or click Load… | Déposer une réponse impulsionnelle ici (WAV / FLAC / MP3) ou cliquer sur Load… | ChamberDeviceBody.cs |
| Dry/Wet — double-click resets | Dry/Wet — double-clic pour réinitialiser | ChamberDeviceBody.cs |
| Ducking — the wet dips (up to 24 dB) while the input plays | Ducking — le signal traité baisse (jusqu’à 24 dB) tant que l’entrée joue | ChamberDeviceBody.cs |
| Eco: IR up to 3 s · Mid: 6 s · High: 10 s + cubic-interpolated algorithm modulation | Eco : réponse jusqu’à 3 s · Mid : 6 s · High : 10 s + modulation de l’algorithme interpolée en cubique | ChamberDeviceBody.cs |
| Freeze — hold the algorithm tail forever; the input is muted | Freeze — maintient la queue de l’algorithme indéfiniment ; l’entrée est coupée | ChamberDeviceBody.cs |
| Hold in — keep feeding the input into the frozen tail (layering) | Hold in — continue d’envoyer l’entrée dans la queue gelée (superposition) | ChamberDeviceBody.cs |
| Impulse response — built-in rooms, or your own file (drop it on the waveform) | Impulse response — pièces intégrées, ou votre propre fichier (le déposer sur la forme d’onde) | ChamberDeviceBody.cs |
| Left handle: low cut (X) + low shelf (Y) · right handle: high cut (X) + high shelf (Y) · ⌥-click to type | Poignée gauche : coupe-bas (X) + shelf grave (Y) · poignée droite : coupe-haut (X) + shelf aigu (Y) · ⌥-clic pour saisir | ChamberDeviceBody.cs |
| Load an impulse response (WAV / FLAC / MP3 — mono, stereo or 4-ch true stereo) | Charger une réponse impulsionnelle (WAV / FLAC / MP3 — mono, stéréo ou vrai stéréo 4 canaux) | ChamberDeviceBody.cs |
| Load impulse response… | Charger une réponse impulsionnelle… | ChamberDeviceBody.cs |
| On: exact convolution timing at 0 ms predelay (FIR head, more CPU). Off: lighter — the first block arrives late and is absorbed into the predelay. | On : timing de convolution exact à 0 ms de predelay (tête FIR, plus de CPU). Off : plus léger — le premier bloc arrive en retard et est absorbé dans le predelay. | ChamberDeviceBody.cs |
| Parallel: both engines hear the input · Serial: the convolution feeds the algorithm | Parallel : les deux moteurs entendent l’entrée · Serial : la convolution alimente l’algorithme | ChamberDeviceBody.cs |
| SHIMMER · PITCH SHIFT | Shimmer · pitch shift | ChamberDeviceBody.cs |
| Shimmer · pitch shift | Shimmer · pitch shift | ChamberDeviceBody.cs |
| Shimmer · shimmer mode only | Shimmer · mode shimmer uniquement | ChamberDeviceBody.cs |
| User IR (none loaded) | Réponse perso (aucune chargée) | ChamberDeviceBody.cs |
| User IR — none loaded | Réponse perso — aucune chargée | ChamberDeviceBody.cs |
| Wet only — 100 % wet, no dry: for a return / send track | Wet only — 100 % traité, sans signal sec : pour une piste de retour / d’envoi | ChamberDeviceBody.cs |
| at the input, before both engines | à l’entrée, avant les deux moteurs | ChamberDeviceBody.cs |
| at the output, dry + wet | à la sortie, sec + traité | ChamberDeviceBody.cs |
| on the tail, before Dry/Wet | sur la queue, avant Dry/Wet | ChamberDeviceBody.cs |
| Feed shimmer back | Réinjecter le shimmer | ChamberDeviceBody.cs |
| Drag a marker to set that band's RT60 (mid = Decay; low / high follow it as ratios). The Damping knob sets the high crossover. · ⌥-click to type | Glisser un repère pour régler le RT60 de cette bande (mid = Decay ; low / high le suivent en rapports). Le potentiomètre Damping règle le crossover haut. · ⌥-clic pour saisir | ChamberDeviceBody.cs |
| Load… | Charger… | ChamberDeviceBody.cs |
| Amount at zero: the voices stand still, a static  | Quantité à zéro : les voix restent immobiles, un  | ChorusDeviceBody.cs |
| Amount — how far the LFO swings each voice's delay: more is wider detune | Amount — l’amplitude du LFO sur le retard de chaque voix : plus c’est grand, plus le désaccord est large | ChorusDeviceBody.cs |
| Chorus bypassed: the signal passes unchanged | Chorus contourné : le signal passe inchangé | ChorusDeviceBody.cs |
| Classic — two voices, left and right; Ensemble — three voices 120° apart, a string-machine shimmer; Vibrato — the pitch wobble alone, no dry signal | Classic — deux voix, gauche et droite ; Ensemble — trois voix à 120°, un scintillement de string machine ; Vibrato — l’oscillation de hauteur seule, sans signal sec | ChorusDeviceBody.cs |
| Detune up to ± | Désaccord jusqu’à ± | ChorusDeviceBody.cs |
| Each voice over two LFO cycles — its delay (τ) or its detune (ct) — and where it is now. Drag up / down for Amount; ⌥-click types, double-click resets it. | Chaque voix sur deux cycles du LFO — son retard (τ) ou son désaccord (ct) — et sa position actuelle. Glisser vers le haut / bas pour Amount ; ⌥-clic pour saisir, double-clic pour réinitialiser. | ChorusDeviceBody.cs |
| Feedback — the voices fed back into the line, ±90 %: a little thickens, past 60 % it rings metallic like a flanger | Feedback — les voix réinjectées dans la ligne, ±90 % : un peu épaissit, au-delà de 60 % le son devient métallique comme un flanger | ChorusDeviceBody.cs |
| HPF — a high-pass on the wet input (20 Hz … 2 kHz), so the lows stay dry and centred; far left is off | HPF — un passe-haut sur l’entrée traitée (20 Hz … 2 kHz), pour que les graves restent secs et centrés ; tout à gauche, il est désactivé | ChorusDeviceBody.cs |
| How many voices sing, or that the feedback makes it ring (past 60 %), or that Vibrato plays wet only | Le nombre de voix qui chantent, ou le fait que le feedback le fait sonner (au-delà de 60 %), ou que Vibrato joue uniquement le signal traité | ChorusDeviceBody.cs |
| Mix — 0 % dry, 50 % dry and wet equal, 100 % the voices alone. Vibrato is always wet | Mix — 0 % sec, 50 % sec et traité à égalité, 100 % les voix seules. Vibrato est toujours traité | ChorusDeviceBody.cs |
| Offset — the right voice's LFO behind the left: 0° moves both together, 180° opposite. Ensemble spreads its three voices 120° apart on its own | Offset — le LFO de la voix droite en retard sur la gauche : 0° les fait bouger ensemble, 180° en opposition. Ensemble espace ses trois voix de 120° de lui-même | ChorusDeviceBody.cs |
| Rate — the note value of one LFO cycle, 4/1 … 1/16 with triplet (T) values | Rate — la valeur de note d’un cycle de LFO, 4/1 … 1/16 avec valeurs en triolets (T) | ChorusDeviceBody.cs |
| The largest detune the sweep reaches, ± cents (0 … 60 on the bar) | Le plus grand désaccord atteint par le balayage, ± cents (0 … 60 sur la barre) | ChorusDeviceBody.cs |
| The range each voice's delay sweeps, ms (0 … 24 on the bar) | La plage balayée par le retard de chaque voix, ms (0 … 24 sur la barre) | ChorusDeviceBody.cs |
| Vibrato: no dry signal, the pitch swings ± | Vibrato : pas de signal sec, la hauteur oscille de ± | ChorusDeviceBody.cs |
| Warmth — a bucket-brigade flavour on the delay line: darker (low-pass down to 2.5 kHz) and gently saturated | Warmth — une saveur à capacités commutées sur la ligne à retard : plus sombre (passe-bas jusqu’à 2,5 kHz) et légèrement saturé | ChorusDeviceBody.cs |
| Where the voices sit in the stereo field (Width) and how far each is detuned right now | Où se placent les voix dans le champ stéréo (Width) et de combien chacune est désaccordée en ce moment | ChorusDeviceBody.cs |
| Width 0 %: the voices fold to mono, no space | Width 0 % : les voix se replient en mono, sans espace | ChorusDeviceBody.cs |
| Width — the voices' stereo spread: 0 % mono, 100 % as they are, 200 % lifts the side by +6 dB | Width — l’étalement stéréo des voix : 0 % mono, 100 % tel quel, 200 % relève le side de +6 dB | ChorusDeviceBody.cs |
| τ draws each voice's delay; ct its detune — how far the moving delay bends the pitch | τ trace le retard de chaque voix ; ct son désaccord — de combien le retard mobile infléchit la hauteur | ChorusDeviceBody.cs |
| Auto gain — make-up that follows the threshold and ratio | Auto gain — un gain de compensation qui suit le seuil et le ratio | CompressorDeviceBody.cs |
| Auto-release — a slower second release once the reduction runs deep, so sustained material does not pump | Auto-release — un second relâchement plus lent quand la réduction est profonde, pour que le matériel soutenu ne pompe pas | CompressorDeviceBody.cs |
| Character — the voicing: Clean as set, Glue slower and softer, Punch faster, Opto slow and program-soft, FET very fast | Character — le voicing : Clean tel que réglé, Glue plus lent et plus doux, Punch plus rapide, Opto lent et doux selon le programme, FET très rapide | CompressorDeviceBody.cs |
| Detector — Peak reacts to every transient, RMS to the body, Auto follows the body and still catches the spikes | Detector — Peak réagit à chaque transitoire, RMS au corps du son, Auto suit le corps tout en captant les pointes | CompressorDeviceBody.cs |
| Drag sideways for the threshold, up / down for the ratio. Double-click resets both. The dot is the key level right now. | Glisser latéralement pour le seuil, vers le haut / bas pour le ratio. Double-clic pour réinitialiser les deux. Le point indique le niveau de la clé en ce moment. | CompressorDeviceBody.cs |
| External key — key off the source picked on the Sidechain tab; off, the track keys itself | External key — déclenche depuis la source choisie dans l’onglet Sidechain ; désactivé, la piste se déclenche elle-même | CompressorDeviceBody.cs |
| Gain reduction over the last four seconds; the dashed line is the peak. Click to clear it. | Réduction de gain sur les quatre dernières secondes ; la ligne en pointillés est la crête. Cliquer pour l’effacer. | CompressorDeviceBody.cs |
| Key gain — drives the detector harder or softer. ⌥-click types, double-click resets. | Key gain — pousse le détecteur plus ou moins fort. ⌥-clic pour saisir, double-clic pour réinitialiser. | CompressorDeviceBody.cs |
| Key source — Internal keys off this track; pick another track, or one pad or chain of it, to duck or pump against it | Key source — Internal se déclenche sur cette piste ; choisir une autre piste, ou l’un de ses pads ou chaînes, pour ducker ou pomper contre elle | CompressorDeviceBody.cs |
| Listen — hear the filtered key instead of the output, to set HP / LP by ear | Listen — écouter la clé filtrée au lieu de la sortie, pour régler HP / LP à l’oreille | CompressorDeviceBody.cs |
| Make-up gain. ⌥-click types, double-click resets. | Gain de compensation. ⌥-clic pour saisir, double-clic pour réinitialiser. | CompressorDeviceBody.cs |
| Stereo link — one reduction for both channels; off, left and right compress on their own | Stereo link — une seule réduction pour les deux canaux ; désactivé, gauche et droite compressent séparément | CompressorDeviceBody.cs |
| The key's spectrum under its filters — drag a handle sideways to move HP / LP, up / down for Q. ⌥-click a handle to type it, double-click to open it. | Le spectre de la clé sous ses filtres — glisser une poignée latéralement pour déplacer HP / LP, vers le haut / bas pour Q. ⌥-clic sur une poignée pour la saisir, double-clic pour l’ouvrir. | CompressorDeviceBody.cs |
| The reduction (neutral ink, hanging from 0 dB) against the input level (teal) — attack is the fall, release the climb back. The dashed line is the threshold. | La réduction (neutre, suspendue à 0 dB) face au niveau d’entrée (turquoise) — l’attaque est la descente, le relâchement la remontée. La ligne en pointillés est le seuil. | CompressorDeviceBody.cs |
| Threshold — the tick is the key's level right now. ⌥-click types, double-click resets. | Threshold — le repère indique le niveau de la clé en ce moment. ⌥-clic pour saisir, double-clic pour réinitialiser. | CompressorDeviceBody.cs |
| Where the source is tapped: before its effects and fader, or after — for a pad or chain, before its own effects, or after them and its level | Où la source est prélevée : avant ses effets et son fader, ou après — pour un pad ou une chaîne, avant ses propres effets, ou après eux et son niveau | CompressorDeviceBody.cs |
| key audible · output muted | clé audible · sortie coupée | CompressorDeviceBody.cs |
| no reduction to measure | aucune réduction à mesurer | CompressorDeviceBody.cs |
|  levels after the fold |  niveaux après le pli | CrushDeviceBody.cs |
| Anti-alias — a 4th-order low-pass at the reduced rate's Nyquist before the hold, so less folds back as images | Anti-alias — un passe-bas d’ordre 4 à la fréquence de Nyquist du taux réduit avant le maintien, pour réduire le repliement en images | CrushDeviceBody.cs |
| Auto gain — rides the output back to the input's loudness (300 ms RMS, ±24 dB), so turning Bits or Drive doesn't jump in level | Auto gain — ramène la sortie au volume de l’entrée (RMS 300 ms, ±24 dB), pour que Bits ou Drive ne fassent pas sauter le niveau | CrushDeviceBody.cs |
| Bits — the bit depth, 1 … 24: fewer bits, fewer levels, more grit | Bits — la profondeur de bits, 1 … 24 : moins de bits, moins de niveaux, plus de grain | CrushDeviceBody.cs |
| DC filter — a 10 Hz high-pass on the crushed signal: takes out the offset a coarse, folded or noisy crush can leave | DC filter — un passe-haut à 10 Hz sur le signal écrasé : retire le décalage continu que peut laisser un crush grossier, replié ou bruité | CrushDeviceBody.cs |
| Dither — up to ±½ LSB of triangular noise before the levels: softens the steps into hiss | Dither — jusqu’à ±½ LSB de bruit triangulaire avant les niveaux : adoucit les marches en souffle | CrushDeviceBody.cs |
| Drive — −12 … +24 dB into the crusher: pushes the signal up the levels, into the Analog clip or into more folds | Drive — −12 … +24 dB dans le crusher : pousse le signal vers les niveaux supérieurs, dans l’écrêtage Analog ou vers davantage de repliements | CrushDeviceBody.cs |
| Input → output through Drive, the mode and the levels; the dot is where the signal peaks. Drag up / down for Drive; ⌥-click types, double-click resets. | Entrée → sortie à travers Drive, le mode et les niveaux ; le point marque la crête du signal. Glisser vers le haut / bas pour Drive ; ⌥-clic pour saisir, double-clic pour réinitialiser. | CrushDeviceBody.cs |
| Jitter — the hold length wanders up to ±50 %: an unsteady, broken clock | Jitter — la durée du maintien varie jusqu’à ±50 % : une horloge instable, irrégulière | CrushDeviceBody.cs |
| Mode — Digital: hard truncation to the levels; Analog: a soft tanh clip before them (+4 dB, rounder); Fold: a wavefolder — more Drive folds the peaks back more times | Mode — Digital : troncature dure aux niveaux ; Analog : un écrêtage tanh doux avant eux (+4 dB, plus rond) ; Fold : un wavefolder — plus de Drive replie les crêtes plus souvent | CrushDeviceBody.cs |
| Noise — a white noise floor under the crush, up to −50 dBFS | Noise — un plancher de bruit blanc sous le crush, jusqu’à −50 dBFS | CrushDeviceBody.cs |
| Output gain, ±12 dB | Gain de sortie, ±12 dB | CrushDeviceBody.cs |
| Peaks in and out, and the output's crest factor (peak over RMS) — the crush flattens it | Crêtes en entrée et en sortie, et facteur de crête de la sortie (crête sur RMS) — le crush l’aplatit | CrushDeviceBody.cs |
| Post filter — a low-pass after the crush, 200 Hz … 20 kHz: under the Nyquist it smooths the steps | Post filter — un passe-bas après le crush, 200 Hz … 20 kHz : sous Nyquist, il lisse les marches | CrushDeviceBody.cs |
| Rate — the reduced sample rate, 500 Hz … full: lower holds each value longer and adds images above its Nyquist | Rate — la fréquence d’échantillonnage réduite, 500 Hz … pleine : plus bas, chaque valeur est tenue plus longtemps et des images apparaissent au-dessus de sa fréquence de Nyquist | CrushDeviceBody.cs |
| The input peak, −60 … 0 dBFS | La crête d’entrée, −60 … 0 dBFS | CrushDeviceBody.cs |
| The output peak, −60 … 0 dBFS | La crête de sortie, −60 … 0 dBFS | CrushDeviceBody.cs |
| The output spectrum: neutral ink is the signal, teal what the crush added — bright above the reduced Nyquist (the images). Drag the Nyquist line for Rate, the filter line for the post filter; ⌥-click types, double-click resets. | Le spectre de sortie : le neutre est le signal, le turquoise ce qu’a ajouté le crush — vif au-dessus du Nyquist réduit (les images). Glisser la ligne de Nyquist pour Rate, la ligne du filtre pour le post filter ; ⌥-clic pour saisir, double-clic pour réinitialiser. | CrushDeviceBody.cs |
| Two cycles of a sine at the driven level (dashed) and what comes out | Deux cycles d’une sinusoïde au niveau d’attaque (pointillés) et ce qui en sort | CrushDeviceBody.cs |
| Wet — the crushed signal against the dry one | Wet — le signal écrasé face au signal sec | CrushDeviceBody.cs |
| What comes out: the bit depth and the rate, and whether the post filter cuts the images above the reduced Nyquist or lets them through | Ce qui sort : la profondeur de bits et la fréquence, et si le post filter coupe les images au-dessus du Nyquist réduit ou les laisse passer | CrushDeviceBody.cs |
| the filter cuts the images | le filtre coupe les images | CrushDeviceBody.cs |
| Tap tempo | Tap tempo | DelayDeviceBody.cs |
| Empty the delay buffer — silences what is still circulating | Vider le tampon de delay — coupe ce qui circule encore | DelayDeviceBody.cs |
| Fade on change — crossfade to a new delay time instead of gliding to it (which bends the pitch, tape style) | Fade on change — fondu enchaîné vers le nouveau temps de delay au lieu d’y glisser (ce qui modifie la hauteur, façon bande) | DelayDeviceBody.cs |
| Feedback — how much of each repeat goes round again; Freeze pins it at 100 %. ⌥-click types, double-click resets. | Feedback — la part de chaque répétition qui repart pour un tour ; Freeze la bloque à 100 %. ⌥-clic pour saisir, double-clic pour réinitialiser. | DelayDeviceBody.cs |
| Freeze — hold the loop forever; the input is muted and the repeats stop decaying | Freeze — maintient la boucle indéfiniment ; l’entrée est coupée et les répétitions cessent de décroître | DelayDeviceBody.cs |
| LOOP HELD · no decay | Boucle maintenue · sans déclin | DelayDeviceBody.cs |
| Link L/R — the right channel follows the left's time | Link L/R — le canal droit suit le temps du gauche | DelayDeviceBody.cs |
| Ping-pong — each repeat crosses to the other channel | Ping-pong — chaque répétition passe sur l’autre canal | DelayDeviceBody.cs |
| Spread — pushes the right channel later, up to 50 ms. ⌥-click types, double-click resets. | Spread — retarde le canal droit, jusqu’à 50 ms. ⌥-clic pour saisir, double-clic pour réinitialiser. | DelayDeviceBody.cs |
| Tap four times to set the delay time by ear (switches to free ms) | Taper quatre fois pour régler le temps de delay à l’oreille (passe en ms libres) | DelayDeviceBody.cs |
| The band the repeats live in — drag the left handle for the low cut, the right one for the high cut. ⌥-click a handle to type it, double-click to open it. | La bande dans laquelle vivent les répétitions — glisser la poignée gauche pour le coupe-bas, la droite pour le coupe-haut. ⌥-clic sur une poignée pour la saisir, double-clic pour l’ouvrir. | DelayDeviceBody.cs |
| The diffuser lengthens the loop by a fixed group delay; on, the read pointer is pulled back by exactly that so each repeat lands on the set time. | Le diffuseur allonge la boucle d’un retard de groupe fixe ; activé, le pointeur de lecture est reculé d’autant pour que chaque répétition tombe sur le temps réglé. | DelayDeviceBody.cs |
| The repeat train — L above the axis, R below. Height is the level left after each pass; the repeat sounding now is lit. | Le train de répétitions — L au-dessus de l’axe, R en dessous. La hauteur est le niveau restant après chaque passage ; la répétition qui sonne en ce moment est allumée. | DelayDeviceBody.cs |
| Wet only — repeats with no dry, for a return / send track | Wet only — répétitions sans signal sec, pour une piste de retour / d’envoi | DelayDeviceBody.cs |
| Clear loop | Effacer la boucle | DelayDeviceBody.cs |
| Attack — how fast the band moves toward its range | Attack — la vitesse à laquelle la bande rejoint sa plage | DynamicEqDeviceBody.cs |
| Band on — off leaves the band out of the signal (double-click its node does the same) | Bande active — désactivée, elle sort du signal (double-clic sur son nœud pour le même effet) | DynamicEqDeviceBody.cs |
| Drag a node — frequency and gain · wheel over a node — Q · double-click a node — band on / off ·  | Glisser un nœud — fréquence et gain · molette sur un nœud — Q · double-clic sur un nœud — bande active / inactive ·  | DynamicEqDeviceBody.cs |
| Dynamic — off parks every band on its static gain: hear what the dynamics do | Dynamic — désactivé, chaque bande reste sur son gain statique : pour entendre ce que font les dynamiques | DynamicEqDeviceBody.cs |
| Flip — cut ↔ boost (a Duck that boosts is upward expansion, a Lift that cuts is downward expansion) | Flip — atténuation ↔ renfort (un Duck qui renforce est une expansion ascendante, un Lift qui atténue est une expansion descendante) | DynamicEqDeviceBody.cs |
| Frequency — or drag the node left / right | Fréquence — ou glisser le nœud à gauche / droite | DynamicEqDeviceBody.cs |
| Gain — the static gain; or drag the node up / down | Gain — le gain statique ; ou glisser le nœud vers le haut / bas | DynamicEqDeviceBody.cs |
| Key — Self: the band listens to this track · Ext: to the key track (click Ext again to pick the track) | Key — Self : la bande écoute cette piste · Ext : la piste de clé (cliquer à nouveau sur Ext pour choisir la piste) | DynamicEqDeviceBody.cs |
| Lift — acts as it falls below it (a boost by default) | Lift — agit quand le signal passe en dessous (un renfort par défaut) | DynamicEqDeviceBody.cs |
| Output — drag up / down (Shift for fine), ⌥-click to type, double-click for 0 dB | Output — glisser vers le haut / bas (Maj pour les pas fins), ⌥-clic pour saisir, double-clic pour 0 dB | DynamicEqDeviceBody.cs |
| Q — the width (the resonance on the cuts); or the wheel over the node | Q — la largeur (la résonance sur les coupes) ; ou la molette sur le nœud | DynamicEqDeviceBody.cs |
| Range — how far the gain can move at full engagement (6 dB past the threshold); click the arrow to flip cut / boost | Range — l’amplitude du gain à plein engagement (6 dB au-delà du seuil) ; cliquer la flèche pour inverser atténuation / renfort | DynamicEqDeviceBody.cs |
| Release — how fast it returns to its static gain | Release — la vitesse de retour à son gain statique | DynamicEqDeviceBody.cs |
| Solo — hear this band alone | Solo — entendre cette bande seule | DynamicEqDeviceBody.cs |
| Static — a plain EQ band · Duck — acts as the band's level rises above the threshold (a cut by default) ·  | Static — une bande d’EQ simple · Duck — agit quand le niveau de la bande dépasse le seuil (une atténuation par défaut) ·  | DynamicEqDeviceBody.cs |
| The selected band's dynamic gain right now | Le gain dynamique de la bande sélectionnée en ce moment | DynamicEqDeviceBody.cs |
| Threshold — where the band starts to act; the teal mark is the band's level now | Threshold — le point où la bande commence à agir ; le repère turquoise est le niveau actuel de la bande | DynamicEqDeviceBody.cs |
| Type — high-pass, low shelf, bell, notch, high shelf, low-pass; only shelves and bells take gain and dynamics | Type — passe-haut, low shelf, bell, notch, high shelf, passe-bas ; seuls les shelves et les bells acceptent gain et dynamique | DynamicEqDeviceBody.cs |
| Crossover slope — Linkwitz-Riley 24 or 48 dB/oct: steeper touches the neighbours less | Crossover slope — Linkwitz-Riley 24 ou 48 dB/oct : plus raide, moins d’effet sur les bandes voisines | Eq3DeviceBody.cs |
| Drag up / down for the band's gain (Shift for fine steps); ⌥-click types it; double-click resets to 0 dB and lifts the kill | Glisser vers le haut / bas pour le gain de la bande (Maj pour les pas fins) ; ⌥-clic pour le saisir ; double-clic pour revenir à 0 dB et lever le kill | Eq3DeviceBody.cs |
| Each band in its colour, the sum in brass, the output spectrum behind. Drag a crossover handle left / right; drag anywhere else to ride that band's gain. Double-click resets, ⌥-click types. | Chaque bande dans sa couleur, la somme en laiton, le spectre de sortie derrière. Glisser une poignée de crossover à gauche / droite ; glisser ailleurs pour régler le gain de cette bande. Double-clic pour réinitialiser, ⌥-clic pour saisir. | Eq3DeviceBody.cs |
| Kill the {0} band — removes it outright, with a 5 ms glide | Kill de la bande {0} — la supprime d’un coup, avec un glissement de 5 ms | Eq3DeviceBody.cs |
| Low / mid crossover | Crossover grave / médium | Eq3DeviceBody.cs |
| Mid / high crossover | Crossover médium / aigu | Eq3DeviceBody.cs |
| Output gain, ±24 dB — drag up / down (Shift for fine), ⌥-click types, double-click resets | Gain de sortie, ±24 dB — glisser vers le haut / bas (Maj pour les pas fins), ⌥-clic pour saisir, double-clic pour réinitialiser | Eq3DeviceBody.cs |
| Analyzer range — the graph's bottom edge, in dB below 0 dBFS | Plage de l’analyseur — le bord inférieur du graphique, en dB sous 0 dBFS | EqDeviceBody.cs |
| Analyzer — Pre: the input spectrum · Post: the output over a dimmer input · Off | Analyseur — Pre : le spectre d’entrée · Post : la sortie au-dessus d’une entrée plus discrète · Off | EqDeviceBody.cs |
| Auto gain — adds the opposite of the shelves' and bells' average level, so edits are judged at the same loudness | Auto gain — ajoute l’opposé du niveau moyen des shelves et des bells, pour juger les modifications à volume égal | EqDeviceBody.cs |
| Channel — St: both channels · Mid: the centre · Side: the edges · L / R: one channel alone | Channel — St : les deux canaux · Mid : le centre · Side : les bords · L / R : un seul canal | EqDeviceBody.cs |
| Enter or Esc to close | Entrée ou Échap pour fermer | EqDeviceBody.cs |
| Gain — or drag the node up / down (Scale multiplies it) | Gain — ou glisser le nœud vers le haut / bas (Scale le multiplie) | EqDeviceBody.cs |
| Label — a short name shown next to the band's node (up to 24 characters) | Label — un nom court affiché près du nœud de la bande (jusqu’à 24 caractères) | EqDeviceBody.cs |
| Labels — show the bands' labels and note marks in the graph (the chips and the band panel keep them) | Labels — affiche les étiquettes et repères de note des bandes dans le graphique (les pastilles et le panneau de bande les conservent) | EqDeviceBody.cs |
| Note — a longer note on what this band is for | Note — une note plus longue sur le rôle de cette bande | EqDeviceBody.cs |
| Output (plus the auto gain in effect) — drag up / down (Shift for fine), ⌥-click to type, double-click for 0 dB | Output (plus l’auto gain en vigueur) — glisser vers le haut / bas (Maj pour les pas fins), ⌥-clic pour saisir, double-clic pour 0 dB | EqDeviceBody.cs |
| Output — the level after the EQ, ±12 dB | Output — le niveau après l’EQ, ±12 dB | EqDeviceBody.cs |
| Peak hold — a thin line at each frequency's highest level of the last moments | Peak hold — une ligne fine au niveau le plus haut de chaque fréquence ces derniers instants | EqDeviceBody.cs |
| Scale — multiplies every shelf and bell gain: 100 % as set, 0 % flat, 200 % twice as much | Scale — multiplie le gain de chaque shelf et bell : 100 % tel que réglé, 0 % à plat, 200 % le double | EqDeviceBody.cs |
| Slope — how steep the high-pass / low-pass falls: 12, 24 or 48 dB per octave | Slope — la raideur du passe-haut / passe-bas : 12, 24 ou 48 dB par octave | EqDeviceBody.cs |
| Type — high-pass, low shelf, bell, notch, high shelf, low-pass; only shelves and bells take gain | Type — passe-haut, low shelf, bell, notch, high shelf, passe-bas ; seuls les shelves et les bells acceptent un gain | EqDeviceBody.cs |
| What this band is for | À quoi sert cette bande | EqDeviceBody.cs |
| Name this band | Nommer cette bande | EqDeviceBody.cs |
| Delay — the centre of the sweep, 0.1 … 8 ms: shorter puts the notches higher | Delay — le centre du balayage, 0,1 … 8 ms : plus court, les encoches montent | FlangerDeviceBody.cs |
| Depth at zero: the comb stands still, a static effect | Depth à zéro : le peigne reste immobile, un effet statique | FlangerDeviceBody.cs |
| Depth — how far the LFO swings the delay around its centre | Depth — l’amplitude du LFO sur le retard autour de son centre | FlangerDeviceBody.cs |
| Feedback +70 %: the classic jet | Feedback +70 % : le jet classique | FlangerDeviceBody.cs |
| Feedback 0: a plain comb | Feedback 0 : un simple peigne | FlangerDeviceBody.cs |
| Feedback — the delayed signal fed back in, ±95 %: positive for the classic jet, negative for a hollow tone | Feedback — le signal retardé réinjecté, ±95 % : positif pour le jet classique, négatif pour un timbre creux | FlangerDeviceBody.cs |
| Feedback −70 %: hollow | Feedback −70 % : creux | FlangerDeviceBody.cs |
| Flanger bypassed: the signal passes unchanged | Flanger contourné : le signal passe inchangé | FlangerDeviceBody.cs |
| How deep the notches cut, dB (0 … −36 on the bar) | Profondeur des encoches, dB (0 … −36 sur la barre) | FlangerDeviceBody.cs |
| Hz runs the LFO free; Sync takes its rate from the tempo and locks it to the bar while the transport plays | Hz fait tourner le LFO librement ; Sync prend sa vitesse du tempo et le cale sur la mesure pendant la lecture | FlangerDeviceBody.cs |
| Mix at zero: only the dry signal is heard | Mix à zéro : seul le signal sec s’entend | FlangerDeviceBody.cs |
| Mix — 50 % gives the deepest notches; 100 % is the delayed signal alone, a vibrato | Mix — 50 % donne les encoches les plus profondes ; 100 % est le signal retardé seul, un vibrato | FlangerDeviceBody.cs |
| Rate — the free LFO rate, 0.02 … 8 Hz | Rate — la vitesse libre du LFO, 0,02 … 8 Hz | FlangerDeviceBody.cs |
| Rate — the note value of one LFO cycle, 2/1 … 1/16 with dotted (D) and triplet (T) values | Rate — la valeur de note d’un cycle de LFO, 2/1 … 1/16 avec valeurs pointées (D) et en triolets (T) | FlangerDeviceBody.cs |
| Stereo — the right LFO against the left: 0° sweeps both together, 90° spreads them, 180° moves them opposite | Stereo — le LFO droit par rapport au gauche : 0° balaie les deux ensemble, 90° les écarte, 180° les fait évoluer en opposition | FlangerDeviceBody.cs |
| The LFO rate: in Hz, or the note value it takes from the tempo | La vitesse du LFO : en Hz, ou la valeur de note prise sur le tempo | FlangerDeviceBody.cs |
| The LFO shape that sweeps the delay: Sine glides, Tri sweeps evenly, Saw rises and drops back | La forme du LFO qui balaie le delay : Sine glisse, Tri balaie régulièrement, Saw monte puis retombe | FlangerDeviceBody.cs |
| The comb the delay carves into the spectrum, L neutral and R teal, with the first notch dashed. Drag left / right to move the notch (Delay), up / down for Feedback; ⌥-click types, double-click resets both. | Le peigne que le delay creuse dans le spectre, L neutre et R turquoise, avec la première encoche en pointillés. Glisser à gauche / droite pour déplacer l’encoche (Delay), vers le haut / bas pour Feedback ; ⌥-clic pour saisir, double-clic pour réinitialiser les deux. | FlangerDeviceBody.cs |
| The delay τ of both channels over two LFO cycles, and where each is now | Le retard τ des deux canaux sur deux cycles du LFO, et la position actuelle de chacun | FlangerDeviceBody.cs |
| The first notch now, over the range the sweep moves it (20 Hz … 20 kHz) | La première encoche actuelle, sur la plage où le balayage la déplace (20 Hz … 20 kHz) | FlangerDeviceBody.cs |
| What the feedback makes: positive sharpens the peaks, negative hollows the tone, past 85 % the comb rings | Effet du feedback : positif, il affine les pics ; négatif, il creuse le timbre ; au-delà de 85 %, le peigne accroche | FlangerDeviceBody.cs |
| Amount — 0 … +30 dB into the stages. Drag the curve up / down for the same | Amount — 0 … +30 dB dans les étages. Glisser la courbe vers le haut / bas pour la même chose | ForgeDeviceBody.cs |
| Bias — the stage's asymmetry, −100 … +100 %: pushes the curve off-centre, adding even harmonics | Bias — l’asymétrie de l’étage, −100 … +100 % : décale la courbe du centre et ajoute des harmoniques paires | ForgeDeviceBody.cs |
| Digital / Fold without oversampling alias: switch on 2× or more | Digital / Fold sans suréchantillonnage produisent de l’aliasing : activer 2× ou plus | ForgeDeviceBody.cs |
| Env → Tone — loud parts push the tone brighter | Env → Tone — les passages forts rendent le timbre plus brillant | ForgeDeviceBody.cs |
| Harmonics 2 … 9 of a −6 dB sine through the curve: odd partials in full ink (hard, buzzy), even ones dimmer (warm, round), and the total harmonic distortion | Harmoniques 2 … 9 d’une sinusoïde à −6 dB passée dans la courbe : impaires en plein (dures, bourdonnantes), paires plus discrètes (chaudes, rondes), et la distorsion harmonique totale | ForgeDeviceBody.cs |
| Input → output through the device (in M/S and Multi through the selected stage): each stage alone faint, the LFO-modulated curve dashed teal, the node where the input sits now. Drag up / down for Amount; ⌥-click to type, double-click resets. | Entrée → sortie à travers l’appareil (en M/S et Multi, à travers l’étage sélectionné) : chaque étage seul en pâle, la courbe modulée par le LFO en turquoise pointillé, le nœud là où se situe l’entrée. Glisser vers le haut / bas pour Amount ; ⌥-clic pour saisir, double-clic pour réinitialiser. | ForgeDeviceBody.cs |
| LFO rate — free 0.05 … 20 Hz, or with Sync a division from 2 bars to 1/64 | LFO rate — libre 0,05 … 20 Hz, ou avec Sync une division de 2 mesures à 1/64 | ForgeDeviceBody.cs |
| LFO → Drive — the LFO sweeps every stage's drive, up to ±30 % | LFO → Drive — le LFO balaie le drive de chaque étage, jusqu’à ±30 % | ForgeDeviceBody.cs |
| Output, −24 … +24 dB | Sortie, −24 … +24 dB | ForgeDeviceBody.cs |
| Oversampling — runs the stages at 2 / 4 / 8 × the rate so the harmonics above Nyquist don't fold back (minimum phase, no latency) | Oversampling — fait tourner les étages à 2 / 4 / 8 × la fréquence pour que les harmoniques au-delà de Nyquist ne se replient pas (phase minimale, sans latence) | ForgeDeviceBody.cs |
| Routing — Serial: 1 → 2 → 3. Parallel: all three from the same input, averaged. M/S: stage 1 drives the Mid, stage 2 the Side, stage 3 the recombined stereo. Multi: stage 1 the lows (< 180 Hz), 2 the mids, 3 the highs (> 2.4 kHz) | Routing — Serial : 1 → 2 → 3. Parallel : les trois depuis la même entrée, moyennés. M/S : l’étage 1 traite le Mid, l’étage 2 le Side, l’étage 3 le stéréo recombiné. Multi : l’étage 1 les graves (< 180 Hz), le 2 les médiums, le 3 les aigus (> 2,4 kHz) | ForgeDeviceBody.cs |
| Stage {0} drive — how hard the stage is pushed (gain 1 … ×21, the level made up) | Drive de l’étage {0} — la force avec laquelle l’étage est poussé (gain 1 … ×21, niveau compensé) | ForgeDeviceBody.cs |
| Stage {0} feedback — its output back into its input (0 … 85 %): thicker, and on the edge of ringing with Fold / Fuzz | Feedback de l’étage {0} — sa sortie réinjectée dans son entrée (0 … 85 %) : plus épais, et à la limite de l’accrochage avec Fold / Fuzz | ForgeDeviceBody.cs |
| Stage {0} on / off | Étage {0} activé / désactivé | ForgeDeviceBody.cs |
| Stage {0} output, −12 … +12 dB — drag up / down, ⌥-click to type, double-click resets | Sortie de l’étage {0}, −12 … +12 dB — glisser vers le haut / bas, ⌥-clic pour saisir, double-clic pour réinitialiser | ForgeDeviceBody.cs |
| Sync — the LFO rate as a note division locked to the song, or free in Hz | Sync — la vitesse du LFO en valeur de note calée sur le morceau, ou libre en Hz | ForgeDeviceBody.cs |
| The selected stage's type — Tube: soft, asymmetric, warm; Tape: soft and even; Diode: hard on one side; Fuzz: squared-off, dense; Fold: folds the peaks back, bright and metallic; Digital: a hard clip | Le type de l’étage sélectionné — Tube : doux, asymétrique, chaud ; Tape : doux et régulier ; Diode : dur d’un côté ; Fuzz : carré, dense ; Fold : replie les crêtes, brillant et métallique ; Digital : un écrêtage dur | ForgeDeviceBody.cs |
| Tone — a tilt after the stage, −12 … +12 dB around 800 Hz: darker or brighter distortion | Tone — une inclinaison après l’étage, −12 … +12 dB autour de 800 Hz : distorsion plus sombre ou plus brillante | ForgeDeviceBody.cs |
| Wet — the forged signal against the dry one | Wet — le signal forgé face au signal sec | ForgeDeviceBody.cs |
| What the device is doing; the master Tone / Bias / Width offsets of older projects show here when set | Ce que fait l’appareil ; les décalages généraux Tone / Bias / Width des anciens projets s’affichent ici lorsqu’ils sont réglés | ForgeDeviceBody.cs |
| Width — the stage's stereo image, 0 … 200 % (in M/S, stage 2's width scales the side) | Width — l’image stéréo de l’étage, 0 … 200 % (en M/S, la largeur de l’étage 2 met à l’échelle le side) | ForgeDeviceBody.cs |
| env → tone {0} | env → tone {0} | ForgeDeviceBody.cs |
| stage {0} · {1} | étage {0} · {1} | ForgeDeviceBody.cs |
| {0} of 3 stages | {0} étages sur 3 | ForgeDeviceBody.cs |
| All-pass stages: every two cut one notch — 2 is a gentle swirl, 12 a deep, dense sweep | Étages passe-tout : tous les deux creusent une encoche — 2 donne un léger tourbillon, 12 un balayage profond et dense | PhaserDeviceBody.cs |
| Center — the middle of the sweep, 50 Hz … 5 kHz: higher moves the notches up | Center — le milieu du balayage, 50 Hz … 5 kHz : plus haut, les encoches montent | PhaserDeviceBody.cs |
| Depth at zero: the notches stand still | Depth à zéro : les encoches restent immobiles | PhaserDeviceBody.cs |
| Depth — how far the LFO swings the stages around Center, up to ±3 octaves | Depth — l’amplitude du LFO sur les étages autour de Center, jusqu’à ±3 octaves | PhaserDeviceBody.cs |
| Feedback — the chain's output fed back in, ±95 %: positive sharpens the peaks, negative moves and widens the notches | Feedback — la sortie de la chaîne réinjectée, ±95 % : positif, il affine les pics ; négatif, il déplace et élargit les encoches | PhaserDeviceBody.cs |
| Mix — 50 % gives the deepest notches; 100 % is the phase-shifted signal alone | Mix — 50 % donne les encoches les plus profondes ; 100 % est le signal déphasé seul | PhaserDeviceBody.cs |
| Phaser bypassed: the signal passes unchanged | Phaser contourné : le signal passe inchangé | PhaserDeviceBody.cs |
| Rate — the note value of one LFO cycle, 4/1 … 1/16 with dotted (D) and triplet (T) values | Rate — la valeur de note d’un cycle de LFO, 4/1 … 1/16 avec valeurs pointées (D) et en triolets (T) | PhaserDeviceBody.cs |
| The LFO shape that sweeps the stages: Sine glides, Tri sweeps evenly, Saw rises and drops back | La forme du LFO qui balaie les étages : Sine glisse, Tri balaie régulièrement, Saw monte puis retombe | PhaserDeviceBody.cs |
| The corner frequency of both channels over two LFO cycles (20 Hz … 20 kHz), and where each is now | La fréquence de coin des deux canaux sur deux cycles du LFO (20 Hz … 20 kHz), et la position actuelle de chacune | PhaserDeviceBody.cs |
| The notches the stages cut into the spectrum, L neutral and R teal, with the corner dashed and each notch ticked on top. Drag left / right to move Center, up / down for Feedback; ⌥-click types, double-click resets both. | Les encoches que les étages creusent dans le spectre, L neutre et R turquoise, avec le coin en pointillés et chaque encoche repérée en haut. Glisser à gauche / droite pour déplacer Center, vers le haut / bas pour Feedback ; ⌥-clic pour saisir, double-clic pour réinitialiser les deux. | PhaserDeviceBody.cs |
| The range the corner sweeps (20 Hz … 20 kHz on the bar), and the first notch now | La plage balayée par le coin (20 Hz … 20 kHz sur la barre), et la première encoche actuelle | PhaserDeviceBody.cs |
| What the feedback makes: positive sharpens the peaks, negative moves and widens the notches, past 85 % the phaser whistles | Effet du feedback : positif, il affine les pics ; négatif, il déplace et élargit les encoches ; au-delà de 85 %, le phaser siffle | PhaserDeviceBody.cs |
| ABOVE — COMPRESS | Au-dessus — compresser | PrismDeviceBody.cs |
| ABOVE — COMPRESSION | Au-dessus — compression | PrismDeviceBody.cs |
| Auto makeup — each band gets back about half of its compression at 0 dBFS | Auto makeup — chaque bande récupère environ la moitié de sa compression à 0 dBFS | PrismDeviceBody.cs |
| Auto release — short peaks recover fast, sustained compression recovers slowly | Auto release — les pics brefs se relâchent vite, une compression soutenue se relâche lentement | PrismDeviceBody.cs |
| BELOW — EXPAND | Au-dessous — expanser | PrismDeviceBody.cs |
| BELOW — EXPANSION | Au-dessous — expansion | PrismDeviceBody.cs |
| Band gain ±24 dB — drag up / down, double-click resets | Gain de bande ±24 dB — glisser vers le haut / bas, double-clic pour réinitialiser | PrismDeviceBody.cs |
| Bands — 3: Low / Mid / High · 2: Low + Mid (Mid runs to 20 kHz) · 1: Mid, full band | Bandes — 3 : Low / Mid / High · 2 : Low + Mid (Mid va jusqu’à 20 kHz) · 1 : Mid, pleine bande | PrismDeviceBody.cs |
| Below — expansion | Au-dessous — expansion | PrismDeviceBody.cs |
| Below — upward compression | Au-dessous — compression ascendante | PrismDeviceBody.cs |
| Compression threshold · ratio (set the ratio in Band detail) | Seuil de compression · ratio (régler le ratio dans Band detail) | PrismDeviceBody.cs |
| Detector — Peak follows every transient; RMS (10 ms) follows loudness, smoother | Detector — Peak suit chaque transitoire ; RMS (10 ms) suit le volume, plus lisse | PrismDeviceBody.cs |
| Expansion / upward-compression threshold · ratio — click the readout to switch it on or off | Seuil d’expansion / compression ascendante · ratio — cliquer sur l’affichage pour l’activer ou le désactiver | PrismDeviceBody.cs |
| Input spectrum (fill) and output (line). Drag a crossover line to move it; ⌥-click to type, double-click resets. Click a band to select it. | Spectre d’entrée (aplat) et de sortie (ligne). Glisser une ligne de crossover pour la déplacer ; ⌥-clic pour saisir, double-clic pour réinitialiser. Cliquer une bande pour la sélectionner. | PrismDeviceBody.cs |
| Listen — hear the detector's key signal (the sidechain, or the input), per solo band | Listen — écouter le signal de clé du détecteur (le sidechain, ou l’entrée), par bande en solo | PrismDeviceBody.cs |
| Listening to the detector key | À l’écoute de la clé du détecteur | PrismDeviceBody.cs |
| None — own input | Aucun — entrée propre | PrismDeviceBody.cs |
| Output gain ±24 dB — double-click: 0 dB | Gain de sortie ±24 dB — double-clic : 0 dB | PrismDeviceBody.cs |
| Ratio above 1 : 1 expands (quiet parts get quieter); below 1 : 1 (↑) compresses upward (quiet parts are lifted). Floor limits either to ± that many dB. | Un ratio au-dessus de 1 : 1 expanse (les passages faibles deviennent plus faibles) ; en dessous de 1 : 1 (↑) il comprime vers le haut (les passages faibles sont relevés). Floor limite les deux à ± ce nombre de dB. | PrismDeviceBody.cs |
| Sidechain — key every band's detector from another track (split into the same bands) | Sidechain — pilote le détecteur de chaque bande depuis une autre piste (découpée dans les mêmes bandes) | PrismDeviceBody.cs |
| Soft clip — a smooth ceiling at −0.3 dBFS after the output gain | Soft clip — un plafond doux à −0,3 dBFS après le gain de sortie | PrismDeviceBody.cs |
| Solo a band — hear only that band (click again to clear) | Solo d’une bande — n’entendre que cette bande (cliquer à nouveau pour annuler) | PrismDeviceBody.cs |
| The selected band's input peaks (grey) and what its compressor's detector makes of them (the last ~1.5 s), against the threshold. | Les crêtes d’entrée de la bande sélectionnée (gris) et ce qu’en fait le détecteur de son compresseur (les ~1,5 dernières secondes), face au seuil. | PrismDeviceBody.cs |
| crossovers {0} / {1} | crossovers {0} / {1} | PrismDeviceBody.cs |
| {0:0.#} kHz · {1}{2} · CPU {3:0} % | {0:0.#} kHz · {1}{2} · CPU {3:0} % | PrismDeviceBody.cs |
| Amount — scales every band's compression and expansion (0 % = none). Double-click: 100 % | Amount — met à l’échelle la compression et l’expansion de chaque bande (0 % = aucune). Double-clic : 100 % | PrismDeviceBody.cs |
| Drag a dot: ← → threshold, ↑ ↓ ratio (down = more). The bright dot is compression above, the grey one expansion below. Double-click a dot resets it. · ⌥-click to type | Glisser un point : ← → seuil, ↑ ↓ ratio (vers le bas = plus). Le point brillant est la compression au-dessus, le gris l’expansion en dessous. Double-clic sur un point pour le réinitialiser. · ⌥-clic pour saisir | PrismDeviceBody.cs |
| Algorithm — Hall (large, spaced reflections) · Room (small, close walls) · Plate (dense, no room at all) · Chamber (between) | Algorithme — Hall (grandes réflexions espacées) · Room (petite pièce, murs proches) · Plate (dense, sans pièce) · Chamber (entre les deux) | ReverbDeviceBody.cs |
| Bass mono — below this frequency the tail is mono, so a wide reverb does not smear the low end (off at the far left) | Bass mono — sous cette fréquence la queue est mono, pour qu’une réverbération large n’empâte pas les graves (désactivé tout à gauche) | ReverbDeviceBody.cs |
| Decay — RT60, the time the tail takes to fall 60 dB (0.2 … 12 s); Freeze holds it forever. Double-click resets. | Decay — RT60, le temps que met la queue à baisser de 60 dB (0,2 … 12 s) ; Freeze la maintient indéfiniment. Double-clic pour réinitialiser. | ReverbDeviceBody.cs |
| Early reflections — the first discrete echoes off the walls, spaced by the algorithm and the size | Early reflections — les premiers échos discrets sur les murs, espacés selon l’algorithme et la taille | ReverbDeviceBody.cs |
| Freeze — hold the tail forever; the input is muted and nothing decays | Freeze — maintient la queue indéfiniment ; l’entrée est coupée et rien ne décroît | ReverbDeviceBody.cs |
| HF Damp — how much faster the highs die than the lows. Double-click resets. | HF Damp — de combien les aigus s’éteignent plus vite que les graves. Double-clic pour réinitialiser. | ReverbDeviceBody.cs |
| Kill tail — empty the reverb at once; whatever is still ringing (or frozen) stops | Kill tail — vide la réverbération d’un coup ; tout ce qui sonne encore (ou est gelé) s’arrête | ReverbDeviceBody.cs |
| Latency comp. | Comp. de latence | ReverbDeviceBody.cs |
| Mod on tail — the modulation wobbles the tail itself (a chorused, evolving decay); off, it moves only the early part and the tail stays still | Mod on tail — la modulation fait osciller la queue elle-même (un déclin chorusé et évolutif) ; désactivé, elle ne bouge que le début et la queue reste immobile | ReverbDeviceBody.cs |
| The decay tail — the impulse at the pre-delay (teal), the early reflections (drawn magnified), then the RT60 curve over 4 s. Drag the teal marker for the pre-delay, drag the tail for the decay; ⌥-click to type, double-click to reset. | La queue de déclin — l’impulsion au pre-delay (turquoise), les premières réflexions (dessinées agrandies), puis la courbe RT60 sur 4 s. Glisser le repère turquoise pour le pre-delay, la queue pour le déclin ; ⌥-clic pour saisir, double-clic pour réinitialiser. | ReverbDeviceBody.cs |
| The input diffuser delays the tail by a fixed group delay; on, the pre-delay is shortened by exactly that so the tail starts on the set pre-delay (it cannot go below zero). | Le diffuseur d’entrée retarde la queue d’un retard de groupe fixe ; activé, le pre-delay est raccourci d’autant pour que la queue démarre au pre-delay réglé (il ne peut pas descendre sous zéro). | ReverbDeviceBody.cs |
| Vintage — an early-digital colour: a band-limited input, 12-bit wet and a deeper wobble | Vintage — une couleur des débuts du numérique : entrée limitée en bande, traitement en 12 bits et oscillation plus profonde | ReverbDeviceBody.cs |
| Wet only — the tail with no dry, for a return / send track | Wet only — la queue sans signal sec, pour une piste de retour / d’envoi | ReverbDeviceBody.cs |
| input muted · tail held | entrée coupée · queue maintenue | ReverbDeviceBody.cs |
| Wet only (send) | Wet only (envoi) | ReverbDeviceBody.cs |
|  · ⌥-click to type |  · ⌥-clic pour saisir | ShutterDeviceBody.cs |
|  → Map (Pattern Out) |  → Map (Pattern Out) | ShutterDeviceBody.cs |
|  → Volume · gated |  → Volume · gaté | ShutterDeviceBody.cs |
| Clock — Sync locks the pattern to the song's bars, Free runs it in Hz | Clock — Sync cale le motif sur les mesures du morceau, Free le fait tourner en Hz | ShutterDeviceBody.cs |
| Complexity — how busy and varied Rnd makes the pattern | Complexity — le degré de densité et de variété que Rnd donne au motif | ShutterDeviceBody.cs |
| Depth — how far an empty step turns the signal down | Depth — de combien un pas vide baisse le signal | ShutterDeviceBody.cs |
| Detector filter — the key goes through the band-pass (Sidechain tab) before the detector | Detector filter — la clé passe par le passe-bande (onglet Sidechain) avant le détecteur | ShutterDeviceBody.cs |
| Detector filter — the key goes through the band-pass before the detector | Detector filter — la clé passe par le passe-bande avant le détecteur | ShutterDeviceBody.cs |
| External sidechain — the detector listens to the key source instead of this track | Sidechain externe — le détecteur écoute la source de clé au lieu de cette piste | ShutterDeviceBody.cs |
| Gain reduction — how far the gate has turned the signal down now | Gain reduction — de combien le gate baisse le signal en ce moment | ShutterDeviceBody.cs |
| Gated — the pattern plays only while the threshold gate is open (gate × pattern) | Gated — le motif ne joue que tant que le gate de seuil est ouvert (gate × motif) | ShutterDeviceBody.cs |
| Key source — Internal keys off this track; pick another track, or one pad or chain of it, to gate or duck against it | Key source — Internal se déclenche sur cette piste ; choisir une autre piste, ou l’un de ses pads ou chaînes, pour gater ou ducker contre elle | ShutterDeviceBody.cs |
| Levels — 4, 8 or 16 heights a step snaps to | Levels — 4, 8 ou 16 hauteurs sur lesquelles un pas se cale | ShutterDeviceBody.cs |
| Listen — hear the key as the detector hears it (after its filter), to tune the band | Listen — écouter la clé comme l’entend le détecteur (après son filtre), pour régler la bande | ShutterDeviceBody.cs |
| Live — the graph follows the audio; click to freeze the picture | Live — le graphique suit l’audio ; cliquer pour figer l’image | ShutterDeviceBody.cs |
| Lookahead — 0, 1 or 5 ms | Lookahead — 0, 1 ou 5 ms | ShutterDeviceBody.cs |
| Lookahead — the gate opens this much before the transient arrives (the track is delayed by it) | Lookahead — le gate s’ouvre aussi tôt avant l’arrivée du transitoire (la piste est retardée d’autant) | ShutterDeviceBody.cs |
| Map → Pattern Out | Map → Pattern Out | ShutterDeviceBody.cs |
| Mode — Gate lets the signal through above the threshold; Duck turns it down while the key is above it | Mode — Gate laisse passer le signal au-dessus du seuil ; Duck le baisse tant que la clé est au-dessus | ShutterDeviceBody.cs |
| Mouse tool for the grid | Outil souris pour la grille | ShutterDeviceBody.cs |
| One opening: attack, hold, release in the chosen shape, down to the floor (inverted for Duck). Drag a node sideways for its time, the teal floor up or down; double-click resets, ⌥-click types. | Une ouverture : attaque, maintien, relâchement dans la forme choisie, jusqu’au plancher (inversé pour Duck). Glisser un nœud latéralement pour son temps, le plancher turquoise vers le haut ou le bas ; double-clic pour réinitialiser, ⌥-clic pour saisir. | ShutterDeviceBody.cs |
| Only while gate open | Uniquement gate ouvert | ShutterDeviceBody.cs |
| Paste copied steps (fitted to the grid) | Coller les pas copiés (ajustés à la grille) | ShutterDeviceBody.cs |
| Pattern shapes — the arrows step through them (fitted to the grid) | Formes de motifs — les flèches les parcourent (ajustées à la grille) | ShutterDeviceBody.cs |
| Peak hold — the detector holds each peak longer, so low notes and slow waves don't chatter the gate | Peak hold — le détecteur maintient chaque crête plus longtemps, pour que les notes graves et les ondes lentes ne fassent pas trembler le gate | ShutterDeviceBody.cs |
| Rate — how long the whole pattern takes: 1/4 … 4 bars in Sync, 4 … 0.25 Hz in Free | Rate — la durée du motif entier : 1/4 … 4 mesures en Sync, 4 … 0,25 Hz en Free | ShutterDeviceBody.cs |
| Reset — clear the peak reduction, the opening count and the history | Reset — efface la réduction maximale, le compte d’ouvertures et l’historique | ShutterDeviceBody.cs |
| Retrig on MIDI note | Redéclencher sur note MIDI | ShutterDeviceBody.cs |
| Retrig — every MIDI note on this track (or the key track) restarts the pattern | Retrig — chaque note MIDI de cette piste (ou de la piste de clé) relance le motif | ShutterDeviceBody.cs |
| Retrigger — trigger mode: each hit fires one attack → hold → release, however long the signal stays above the threshold | Retrigger — mode déclenché : chaque frappe lance une attaque → maintien → relâchement, quelle que soit la durée pendant laquelle le signal reste au-dessus du seuil | ShutterDeviceBody.cs |
| Return — how far under the threshold the key must fall before the gate closes (stops chatter) | Return — de combien la clé doit passer sous le seuil avant que le gate ne se ferme (évite les battements) | ShutterDeviceBody.cs |
| Roll a new pattern — Complexity sets how busy and how varied it is | Tirer un nouveau motif — Complexity règle sa densité et sa variété | ShutterDeviceBody.cs |
| Shape — Linear: straight ramps over the times; Log: the classic one-pole curve; Snap: stays open, then shuts hard | Shape — Linear : rampes droites sur les durées ; Log : la courbe classique à un pôle ; Snap : reste ouvert, puis se ferme net | ShutterDeviceBody.cs |
| Signal tab: click to change the window —  | Onglet Signal : cliquer pour changer la fenêtre —  | ShutterDeviceBody.cs |
| Smooth — ramps into each step instead of jumping (up to half a step) | Smooth — monte en rampe vers chaque pas au lieu de sauter (jusqu’à un demi-pas) | ShutterDeviceBody.cs |
| Steps — 8, 16 or 32 (the pattern is stretched to fit) | Steps — 8, 16 ou 32 (le motif est étiré pour s’y adapter) | ShutterDeviceBody.cs |
| Swing — every second step comes late | Swing — un pas sur deux arrive en retard | ShutterDeviceBody.cs |
| Target — Off; Volume: the pattern gates the audio; Map: the audio is left alone and the pattern drives Pattern Out, a CV source in the Modular view | Target — Off ; Volume : le motif gate l’audio ; Map : l’audio n’est pas touché et le motif pilote Pattern Out, une source CV de la vue Modulaire | ShutterDeviceBody.cs |
| The detector's band-pass (teal) — only this part of the key opens the gate — over the reduction in the window (neutral ink). Drag the nodes sideways; double-click resets, ⌥-click types. | Le passe-bande du détecteur (turquoise) — seule cette partie de la clé ouvre le gate — au-dessus de la réduction dans la fenêtre (neutre). Glisser les nœuds latéralement ; double-clic pour réinitialiser, ⌥-clic pour saisir. | ShutterDeviceBody.cs |
| The input (teal) and what passes (neutral ink) over the window, with the threshold (dashed) and where the gate closes again (dashed). Drag a line up or down; double-click resets it, ⌥-click types it. | L’entrée (turquoise) et ce qui passe (neutre) sur la fenêtre, avec le seuil (pointillés) et le point où le gate se referme (pointillés). Glisser une ligne vers le haut ou le bas ; double-clic pour la réinitialiser, ⌥-clic pour la saisir. | ShutterDeviceBody.cs |
| The level the detector compares — this track's input, or the key when an external sidechain drives it; the dashed tick is the threshold | Le niveau que compare le détecteur — l’entrée de cette piste, ou la clé quand un sidechain externe le pilote ; le repère en pointillés est le seuil | ShutterDeviceBody.cs |
| The step pattern — each column is one step's level, the line is the gain it makes (smooth and swing bend it). Draw sets steps, Line draws a ramp, Erase clears, Flip toggles on / off. | Le motif de pas — chaque colonne est le niveau d’un pas, la ligne est le gain qui en résulte (smooth et swing l’infléchissent). Draw règle les pas, Line trace une rampe, Erase efface, Flip bascule actif / inactif. | ShutterDeviceBody.cs |
| What the gate is doing now — closed, attack, open, hold or release — and its gain | Ce que fait le gate en ce moment — fermé, attaque, ouvert, maintien ou relâchement — et son gain | ShutterDeviceBody.cs |
| Clear every step | Effacer tous les pas | ShutterDeviceBody.cs |
| Copy the steps | Copier les pas | ShutterDeviceBody.cs |
| closes to silence | se ferme jusqu’au silence | ShutterDeviceBody.cs |
| ducks to silence | descend jusqu’au silence | ShutterDeviceBody.cs |
| Floor — the level when closed (−∞ = silence); in Duck, how far the signal goes down | Floor — le niveau une fois fermé (−∞ = silence) ; en Duck, de combien le signal descend | ShutterDeviceBody.cs |
| Range — how far the gate closes or the duck goes down (−∞ = all the way) | Range — de combien le gate se ferme ou le duck descend (−∞ = jusqu’au bout) | ShutterDeviceBody.cs |
| SC EQ — the detector hears the key through its band-pass; off = the full band | SC EQ — le détecteur entend la clé à travers son passe-bande ; off = pleine bande | ShutterDeviceBody.cs |
| Press Record to lay down the first loop | Appuyer sur Enregistrer pour poser la première boucle | StrataDeviceBody.cs |
| Set tempo from loop | Régler le tempo d’après la boucle | StrataDeviceBody.cs |
| {0} of {1} · one cycle shown | {0} sur {1} · un cycle affiché | StrataDeviceBody.cs |
| at bar end | en fin de mesure | StrataDeviceBody.cs |
| of {0} bars | sur {0} mesure / sur {0} mesures | StrataDeviceBody.cs |
| onto layer {0} | sur la couche {0} | StrataDeviceBody.cs |
| Channel — Stereo; Left or Right on both sides; Swap the sides | Channel — Stereo ; Left ou Right des deux côtés ; Swap échange les côtés | UtilityDeviceBody.cs |
| Gain match — moves Gain once so the output meets the reference (the input's level, or Target) over the last 3 s, in the meter's unit | Gain match — règle Gain une fois pour que la sortie atteigne la référence (le niveau d’entrée, ou Target) sur les 3 dernières secondes, dans l’unité du vumètre | UtilityDeviceBody.cs |
| Input (teal) and output (neutral ink) over the last 8 s in the meter's unit; dashed: the target. Drag up or down for the target. | Entrée (turquoise) et sortie (neutre) sur les 8 dernières secondes dans l’unité du vumètre ; en pointillés : la cible. Glisser vers le haut ou le bas pour la cible. | UtilityDeviceBody.cs |
| Input peak, −60 … 0 dBFS | Crête d’entrée, −60 … 0 dBFS | UtilityDeviceBody.cs |
| Invert the left channel's phase | Inverser la phase du canal gauche | UtilityDeviceBody.cs |
| Invert the right channel's phase | Inverser la phase du canal droit | UtilityDeviceBody.cs |
| Meter — LUFS-S: short-term loudness (3 s, K-weighted); Peak: sample peak; RMS: 300 ms | Meter — LUFS-S : volume à court terme (3 s, pondération K) ; Peak : crête d’échantillon ; RMS : 300 ms | UtilityDeviceBody.cs |
| Mono below — removes the side below the cutoff, so the low end is mono | Mono below — retire le signal latéral sous la coupure, pour que les graves soient mono | UtilityDeviceBody.cs |
| Output correlation — +1 mono, 0 unrelated, below 0 out of phase (it cancels in mono) | Corrélation de sortie — +1 mono, 0 sans rapport, sous 0 en opposition de phase (s’annule en mono) | UtilityDeviceBody.cs |
| Output peak, −60 … 0 dBFS | Crête de sortie, −60 … 0 dBFS | UtilityDeviceBody.cs |
| Reset — Gain back to 0 dB, and the meters, holds and the level history start over | Reset — Gain revient à 0 dB, et les vumètres, maintiens et l’historique des niveaux repartent de zéro | UtilityDeviceBody.cs |
| Slope of the mono cutoff — 6, 12 or 24 dB/oct; click to step | Pente de la coupure mono — 6, 12 ou 24 dB/oct ; cliquer pour changer | UtilityDeviceBody.cs |
| The input's level in the meter's unit (LUFS-S, 1 s peak or 300 ms RMS) | Le niveau de l’entrée dans l’unité du vumètre (LUFS-S, crête 1 s ou RMS 300 ms) | UtilityDeviceBody.cs |
| The output's level in the meter's unit | Le niveau de la sortie dans l’unité du vumètre | UtilityDeviceBody.cs |
| The output's stereo field by frequency over the last second — lows at the bottom, highs at the top. Solid: where each band sits between L and R; teal: how far it spreads; dashed: how far the width setting spreads an uncorrelated band. Drag sideways for the balance, up or down for the width. | Le champ stéréo de la sortie par fréquence sur la dernière seconde — graves en bas, aigus en haut. Plein : la position de chaque bande entre L et R ; turquoise : son étalement ; pointillés : l’étalement que le réglage de largeur donne à une bande non corrélée. Glisser latéralement pour la balance, vers le haut ou le bas pour la largeur. | UtilityDeviceBody.cs |
| The usual cutoffs — 60, 120 or 240 Hz (turns Mono below on) | Les coupures usuelles — 60, 120 ou 240 Hz (active Mono below) | UtilityDeviceBody.cs |
| The width over frequency — solid: what the settings do (0 % = mono, 100 % = unchanged), the mono region shaded; teal dashed: the output's measured width (side against mid). Drag sideways for the mono cutoff, up or down for the width. | La largeur selon la fréquence — plein : ce que font les réglages (0 % = mono, 100 % = inchangé), la zone mono ombrée ; turquoise en pointillés : la largeur mesurée de la sortie (side contre mid). Glisser latéralement pour la coupure mono, vers le haut ou le bas pour la largeur. | UtilityDeviceBody.cs |
| What Gain match and Level match aim for — the input's level, or Target | Ce que visent Gain match et Level match — le niveau de l’entrée, ou Target | UtilityDeviceBody.cs |
| out of phase — mono risk | en opposition de phase — risque en mono | UtilityDeviceBody.cs |
| pan by frequency · 1 s window | panoramique par fréquence · fenêtre de 1 s | UtilityDeviceBody.cs |
| Mono below freq | Mono below freq | UtilityDeviceBody.cs |
| Mute the output | Couper la sortie | UtilityDeviceBody.cs |
| bass mono off | bass mono désactivé | UtilityDeviceBody.cs |
| both phases inverted | les deux phases inversées | UtilityDeviceBody.cs |
| match to input | aligner sur l’entrée | UtilityDeviceBody.cs |
| Width law — L/R: the side is scaled, so wider is louder; M/S: mid and side are traded, so the level holds (200 % = side only) | Width law — L/R : le side est mis à l’échelle, donc plus large est plus fort ; M/S : mid et side sont échangés, donc le niveau reste constant (200 % = side seul) | UtilityDeviceBody.cs |
| Bright — a treble lift before the preamp, strongest at low gain (the bright cap on a volume pot) | Bright — une montée des aigus avant le préampli, plus forte à faible gain (le condensateur bright d’un potentiomètre de volume) | ValveDeviceBody.cs |
| Cabinet — Match follows the model; 1×12 open, 2×12 combo, 4×12 closed, 1×15 bass | Cabinet — Match suit le modèle ; 1×12 ouvert, 2×12 combo, 4×12 fermé, 1×15 basse | ValveDeviceBody.cs |
| Cabinet — Match follows the model; the others bring their own low resonance and presence peak. Scroll to step. | Cabinet — Match suit le modèle ; les autres apportent leur propre résonance grave et leur pic de présence. Défiler pour changer. | ValveDeviceBody.cs |
| Deep — a low resonance boost after the tone stack, for a fuller, tighter bottom | Deep — un renfort de résonance grave après le correcteur de tonalité, pour des graves plus pleins et plus serrés | ValveDeviceBody.cs |
| Even only — the preamp adds even harmonics only: warmth without the fizz of the odd ones | Even only — le préampli n’ajoute que des harmoniques paires : de la chaleur sans le grésillement des impaires | ValveDeviceBody.cs |
| Gain — how hard the signal hits the preamp. Double-click resets. | Gain — la force avec laquelle le signal attaque le préampli. Double-clic pour réinitialiser. | ValveDeviceBody.cs |
| Mic position — at the cone's cap (bright) or its edge (darker) | Mic position — au centre du cône (brillant) ou sur son bord (plus sombre) | ValveDeviceBody.cs |
| Mic — Dynamic: the classic close mic; Condenser: brighter, less proximity bass; Ribbon: dark, the most proximity bass | Mic — Dynamic : le micro proche classique ; Condenser : plus brillant, moins d’effet de proximité ; Ribbon : sombre, le plus d’effet de proximité | ValveDeviceBody.cs |
| Model — the amp's voicing: how hard the preamp drives, how many stages clip, how tight the low end is and which cabinet it matches. Scroll to step. | Model — le voicing de l’ampli : la force d’attaque du préampli, le nombre d’étages qui écrêtent, la fermeté des graves et le baffle associé. Défiler pour changer. | ValveDeviceBody.cs |
| Output — the master level after the cabinet, ±12 dB. Double-click resets. | Output — le niveau général après le baffle, ±12 dB. Double-clic pour réinitialiser. | ValveDeviceBody.cs |
| The cabinet as the mic hears it, with the low / high cuts — dashed is the same cab and mic on axis at the cap, 5 cm away. Drag sideways to move the mic off axis, up to bring it closer. | Le baffle tel que l’entend le micro, avec les coupures grave / aigu — en pointillés le même baffle et micro dans l’axe, au centre du cône, à 5 cm. Glisser latéralement pour désaxer le micro, vers le haut pour le rapprocher. | ValveDeviceBody.cs |
| The harmonics the preamp adds at the input's level (the fundamental at the top). Drag up or down for the gain. | Les harmoniques ajoutées par le préampli au niveau de l’entrée (la fondamentale en haut). Glisser vers le haut ou le bas pour le gain. | ValveDeviceBody.cs |
| The tone stack before the cabinet — the pre-distortion tightness of the model, Bright, Bass / Middle / Treble / Presence and Deep; dashed is the stack flat. Drag sideways for the middle's frequency, up or down for the middle. | Le correcteur de tonalité avant le baffle — la fermeté avant distorsion du modèle, Bright, Bass / Middle / Treble / Presence et Deep ; en pointillés, le correcteur à plat. Glisser latéralement pour la fréquence du médium, vers le haut ou le bas pour le médium. | ValveDeviceBody.cs |
| cab by model ·  | baffle selon le modèle ·  | ValveDeviceBody.cs |
| Even harmonics only | Harmoniques paires uniquement | ValveDeviceBody.cs |
| Analog stage — a console-style soft clip after the saturation (click again for none) | Étage analogique — un écrêtage doux façon console après la saturation (cliquer à nouveau pour aucun) | VintageDeviceBody.cs |
| Auto-comp — the output level follows the input's, however hard the drive | Auto-comp — le niveau de sortie suit celui de l’entrée, quelle que soit la saturation | VintageDeviceBody.cs |
| Character — the era the signal passes through: its saturation, tone, band-limit and how much hiss, crackle, wow and flutter it brings. Scroll to step. | Character — l’époque que traverse le signal : sa saturation, sa tonalité, sa bande passante et la quantité de souffle, de craquements, de wow et de flutter. Défiler pour changer. | VintageDeviceBody.cs |
| Drive — how hard the signal hits the saturation. ⌥-click types, double-click resets. | Drive — la force avec laquelle le signal attaque la saturation. ⌥-clic pour saisir, double-clic pour réinitialiser. | VintageDeviceBody.cs |
| Sync — the wow follows the tempo: one sway every 4 bars … 1/8 (RATE picks it) | Sync — le wow suit le tempo : une oscillation toutes les 4 mesures … 1/8 (RATE la choisit) | VintageDeviceBody.cs |
| The harmonics the saturation adds at the input's level (the fundamental at the top), against the hiss floor in teal. Drag up or down for the drive, ⌥-click to type it. | Les harmoniques ajoutées par la saturation au niveau de l’entrée (la fondamentale en haut), face au plancher de souffle en turquoise. Glisser vers le haut ou le bas pour le drive, ⌥-clic pour le saisir. | VintageDeviceBody.cs |
| The pitch drift over the last four seconds — wow in neutral ink, flutter in teal. Drag up or down for the depth of what is picked above, sideways for its rate; ⌥-click to type them. | La dérive de hauteur sur les quatre dernières secondes — wow en neutre, flutter en turquoise. Glisser vers le haut ou le bas pour la profondeur de ce qui est choisi au-dessus, latéralement pour sa vitesse ; ⌥-clic pour les saisir. | VintageDeviceBody.cs |
| The transfer curve the saturation runs — input level across, output up; dashed is the dry line, teal what you hear with Mix and Output, the dot where the input peak sits. Drag up or down for the drive, ⌥-click to type it. | La courbe de transfert de la saturation — niveau d’entrée en abscisse, sortie en ordonnée ; en pointillés la ligne sèche, en turquoise ce qu’on entend avec Mix et Output, le point où se situe la crête d’entrée. Glisser vers le haut ou le bas pour le drive, ⌥-clic pour le saisir. | VintageDeviceBody.cs |
| Tone model — Warm: the era's own tilt and head bump; Flat: the TONE knob alone; Dark: a lower, darker tilt | Tone model — Warm : l’inclinaison propre à l’époque et la bosse de tête ; Flat : le potentiomètre TONE seul ; Dark : une inclinaison plus basse et plus sombre | VintageDeviceBody.cs |
| Tube stage — a warm, asymmetric valve stage after the saturation (click again for none) | Étage à lampe — un étage à lampe chaud et asymétrique après la saturation (cliquer à nouveau pour aucun) | VintageDeviceBody.cs |
| Wear — the age of the medium: a narrower band, more hiss and crackle, a wandering flutter. ⌥-click types, double-click resets. | Wear — l’âge du support : une bande plus étroite, plus de souffle et de craquements, un flutter erratique. ⌥-clic pour saisir, double-clic pour réinitialiser. | VintageDeviceBody.cs |
| What the window shows and a drag in it edits | Ce que montre la fenêtre et ce qu’un glissement y modifie | VintageDeviceBody.cs |
| Wear follows input | Wear suit l’entrée | VintageDeviceBody.cs |
| Attack — how fast a band opens, 0.5 … 100 ms | Attack — la vitesse d’ouverture d’une bande, 0,5 … 100 ms | VocoderDeviceBody.cs |
| Bands — how many bands, 8 … 32: fewer is more robotic, more is clearer | Bands — le nombre de bandes, 8 … 32 : moins, c’est plus robotique ; plus, c’est plus clair | VocoderDeviceBody.cs |
| Carrier track — the track the voice shapes when Carrier is Track; this track and its groups are not offered | Piste porteuse — la piste que la voix module quand Carrier est Track ; cette piste et ses groupes ne sont pas proposés | VocoderDeviceBody.cs |
| Carrier — what the voice shapes: an internal saw (at Pitch), noise, or another track picked below | Carrier — ce que la voix module : une dent de scie interne (à Pitch), du bruit, ou une autre piste choisie ci-dessous | VocoderDeviceBody.cs |
| Formant — moves the carrier's bands against the modulator's, ±12 st: up sounds smaller, down larger | Formant — décale les bandes de la porteuse par rapport à celles du modulateur, ±12 st : vers le haut, le son paraît plus petit ; vers le bas, plus grand | VocoderDeviceBody.cs |
| Gain — the output level, ±12 dB | Gain — le niveau de sortie, ±12 dB | VocoderDeviceBody.cs |
| High — the highest band's edge, 2 … 20 kHz | High — le bord de la bande la plus haute, 2 … 20 kHz | VocoderDeviceBody.cs |
| Hold — freezes every band where it is; the carrier keeps sounding through it | Hold — fige chaque bande là où elle est ; la porteuse continue de sonner à travers | VocoderDeviceBody.cs |
| Low — the lowest band's edge, 20 Hz … 1 kHz | Low — le bord de la bande la plus basse, 20 Hz … 1 kHz | VocoderDeviceBody.cs |
| Mix — the carrier as it came in against the vocoded sound | Mix — la porteuse telle qu’elle arrive face au son vocodé | VocoderDeviceBody.cs |
| Mod gain — trims the voice before the bands hear it, ±24 dB | Mod gain — ajuste la voix avant que les bandes ne l’entendent, ±24 dB | VocoderDeviceBody.cs |
| Pitch — the internal saw's note, C0 … C7 | Pitch — la note de la dent de scie interne, C0 … C7 | VocoderDeviceBody.cs |
| Q — how narrow each band is, 1 … 10 | Q — l’étroitesse de chaque bande, 1 … 10 | VocoderDeviceBody.cs |
| Release — how fast a band closes, 5 … 1000 ms | Release — la vitesse de fermeture d’une bande, 5 … 1000 ms | VocoderDeviceBody.cs |
| The modulator is the sound of the track the vocoder sits on — the voice | Le modulateur est le son de la piste sur laquelle se trouve le vocoder — la voix | VocoderDeviceBody.cs |
| The modulator's level in each band (bars) and the carrier's (teal). Drag an edge to move Low or High; ⌥-click an edge to type it, double-click resets it. | Le niveau du modulateur dans chaque bande (barres) et celui de la porteuse (turquoise). Glisser un bord pour déplacer Low ou High ; ⌥-clic sur un bord pour le saisir, double-clic pour le réinitialiser. | VocoderDeviceBody.cs |
| The vocoder's output level | Le niveau de sortie du vocoder | VocoderDeviceBody.cs |
| The voice's level after Mod gain | Le niveau de la voix après Mod gain | VocoderDeviceBody.cs |
| Tilt — brightens (or darkens) the saw or the carrier track around 1 kHz before the bands, ±12 dB | Tilt — éclaircit (ou assombrit) la dent de scie ou la piste porteuse autour de 1 kHz avant les bandes, ±12 dB | VocoderDeviceBody.cs |
| Unvoiced — adds noise where the modulator hisses, so consonants stay clear | Unvoiced — ajoute du bruit là où le modulateur siffle, pour que les consonnes restent claires | VocoderDeviceBody.cs |
| Where the carrier track is tapped: before its effects and fader, or after. Pre keeps feeding while the track is muted | Où la piste porteuse est prélevée : avant ses effets et son fader, ou après. Pre continue d’alimenter même piste coupée | VocoderDeviceBody.cs |
|  · drag to change, double-click to reset |  · glisser pour modifier, double-clic pour réinitialiser | AuroraInstrumentCard.cs |
| Click for the next macros, right-click for the previous | Cliquer pour les macros suivantes, clic droit pour les précédentes | AuroraInstrumentCard.cs |
| Click for the next target, right-click for the previous | Cliquer pour la cible suivante, clic droit pour la précédente | AuroraInstrumentCard.cs |
| SHAPE · LFO 1 | Shape · LFO 1 | AuroraInstrumentCard.cs |
| Shape · LFO {0} | Shape · LFO {0} | AuroraInstrumentCard.cs |
| The oscillator whose table and warp mode this strip edits | L’oscillateur dont cette tranche modifie la table et le mode de warp | AuroraInstrumentCard.cs |
| Zero every route in the matrix | Remettre à zéro toutes les routes de la matrice | AuroraInstrumentCard.cs |
| drag a cell — up adds, down subtracts · ⌥-click types · double-click clears | glisser une cellule — vers le haut ajoute, vers le bas retranche · ⌥-clic pour saisir · double-clic pour effacer | AuroraInstrumentCard.cs |
| {0} / 8 in use | {0} / 8 utilisés | AuroraInstrumentCard.cs |
| Clear all | Tout effacer | AuroraInstrumentCard.cs |
| Mod wheel — opens the LFO onto the cutoff | Mod wheel — ouvre le LFO sur la coupure | BassInstrumentCard.cs |
| Slide into a note played over a held one without a new attack | Glisser vers une note jouée par-dessus une note tenue, sans nouvelle attaque | BassInstrumentCard.cs |
| → cut/pitch · wheel | → coupure/hauteur · molette | BassInstrumentCard.cs |
| A cable breaks the normal at Gate / Filt in / VCA in / Ext in · loops run with a one-sample delay | Un câble coupe la connexion normalisée sur Gate / Filt in / VCA in / Ext in · les boucles tournent avec un échantillon de retard | ConsortInstrumentCard.cs |
| Analog delay · BBD | Delay analogique · BBD | ConsortInstrumentCard.cs |
| Attenuators scale whatever is patched into Atten 1/2 in; Sum adds its inputs | Les atténuateurs mettent à l’échelle ce qui est branché sur Atten 1/2 in ; Sum additionne ses entrées | ConsortInstrumentCard.cs |
| Clock out (patch bay) | Clock out (patch bay) | ConsortInstrumentCard.cs |
| Depth — drag up / down (⌥-click to type, double-click: +50) | Depth — glisser vers le haut / bas (⌥-clic pour saisir, double-clic : +50) | ConsortInstrumentCard.cs |
| Drag jack to jack · Alt-click to pull · right-click for a list | Glisser de prise à prise · Alt-clic pour retirer · clic droit pour une liste | ConsortInstrumentCard.cs |
| EXT: normalled to the VCA output (Moog feedback) — patch Ext in to replace it | EXT : normalisé sur la sortie du VCA (réinjection façon Moog) — brancher Ext in pour le remplacer | ConsortInstrumentCard.cs |
| Filt 1 cutoff input | Entrée de coupure Filt 1 | ConsortInstrumentCard.cs |
| Filt 2 cutoff input | Entrée de coupure Filt 2 | ConsortInstrumentCard.cs |
| Gated glide: only between overlapping (legato) notes | Glide conditionnel : seulement entre notes qui se chevauchent (legato) | ConsortInstrumentCard.cs |
| Jack strip · {0} points ·  | Bandeau de prises · {0} points ·  | ConsortInstrumentCard.cs |
| LFO out (patch bay) | LFO out (patch bay) | ConsortInstrumentCard.cs |
| LFO pitch → all oscillators, or only 2 and 4 | LFO pitch → tous les oscillateurs, ou seulement 2 et 4 | ConsortInstrumentCard.cs |
| LFO rate input (patch bay) | Entrée de vitesse LFO (patch bay) | ConsortInstrumentCard.cs |
| Matrix view · drag a cell for depth, ⌥-click to type it, double-click to remove ·  | Vue matrice · glisser une cellule pour la profondeur, ⌥-clic pour la saisir, double-clic pour supprimer ·  | ConsortInstrumentCard.cs |
| Off · SEQ (steps transposed by the key) · ARP (the held chord over 1–3 octaves) | Off · SEQ (pas transposés par la touche) · ARP (l’accord tenu sur 1 à 3 octaves) | ConsortInstrumentCard.cs |
| Open the full patch bay (all 42 points) · on the Patch tab: back to the last panel | Ouvrir le patch bay complet (les 42 points) · dans l’onglet Patch : retour au dernier panneau | ConsortInstrumentCard.cs |
| Osc {0} pitch input (patch bay) | Entrée de hauteur Osc {0} (patch bay) | ConsortInstrumentCard.cs |
| Oscillator {0}'s note (paraphonic) · the latest voices in true poly | Note de l’oscillateur {0} (paraphonique) · les dernières voix en vrai poly | ConsortInstrumentCard.cs |
| Paint {0} steps (Alt-click a step: rest) | Peindre {0} pas (Alt-clic sur un pas : silence) | ConsortInstrumentCard.cs |
| Pulse width of the pulse wave (square stays 50 %) | Largeur d’impulsion de l’onde pulse (le carré reste à 50 %) | ConsortInstrumentCard.cs |
| Ratchet repeats per step | Répétitions de ratchet par pas | ConsortInstrumentCard.cs |
| Seq gate out (patch bay) | Seq gate out (patch bay) | ConsortInstrumentCard.cs |
| Sequence length — drag up / down | Longueur de la séquence — glisser vers le haut / bas | ConsortInstrumentCard.cs |
| Sequencer off · pick SEQ or ARP, then hold a key | Séquenceur désactivé · choisir SEQ ou ARP, puis tenir une touche | ConsortInstrumentCard.cs |
| Step rate (synced to the host tempo) | Vitesse des pas (synchronisée au tempo de l’hôte) | ConsortInstrumentCard.cs |
| {0} cables · {1} points | {0} câbles · {1} points | ConsortInstrumentCard.cs |
| {0} · depth {1:+0;−0;0} | {0} · depth {1:+0;−0;0} | ConsortInstrumentCard.cs |
| {0} · sync to host {1:0} BPM · CPU {2:0} % | {0} · synchro à l’hôte {1:0} BPM · CPU {2:0} % | ConsortInstrumentCard.cs |
| {0} · ×{1} OS · 0 smp · CPU {2:0} % | {0} · ×{1} OS · 0 éch. · CPU {2:0} % | ConsortInstrumentCard.cs |
| True poly (16) | Vrai poly (16) | ConsortInstrumentCard.cs |
| all normals intact | toutes les connexions normalisées intactes | ConsortInstrumentCard.cs |
| {0} cables | {0} câble / {0} câbles | ConsortInstrumentCard.cs |
| Remove {0}  {1:+0;−0;0} | Retirer {0}  {1:+0;−0;0} | ConsortInstrumentCard.cs |
| Choose the track Flux listens to — or one pad, chain or voice of it | Choisir la piste écoutée par Flux — ou l’un de ses pads, chaînes ou voix | FluxInstrumentCard.cs |
| Click to change the Motion sync rate | Cliquer pour changer la valeur de synchro de Motion | FluxInstrumentCard.cs |
| Drag to blend the four timbres · ⌥-click to type · double-click to reset | Glisser pour mélanger les quatre timbres · ⌥-clic pour saisir · double-clic pour réinitialiser | FluxInstrumentCard.cs |
| Target — what the reaction drives | Target — ce que pilote la réaction | FluxInstrumentCard.cs |
| cutoff +{0} of range | coupure +{0} de la plage | FluxInstrumentCard.cs |
| dashed — where the reaction pulls | pointillés — où tire la réaction | FluxInstrumentCard.cs |
| the synth listens to a track | le synthé écoute une piste | FluxInstrumentCard.cs |
| {0} transients per bar | {0} transitoire par mesure / {0} transitoires par mesure | FluxInstrumentCard.cs |
| → assign a source to turn the reaction on | → assigner une source pour activer la réaction | FluxInstrumentCard.cs |
| → the sidechain bends the pitch up | → le sidechain fait monter la hauteur | FluxInstrumentCard.cs |
| → the sidechain drags the vector in rhythm | → le sidechain entraîne le vecteur en rythme | FluxInstrumentCard.cs |
| → the sidechain opens the filter | → le sidechain ouvre le filtre | FluxInstrumentCard.cs |
| → the sidechain swells the space | → le sidechain gonfle l’espace | FluxInstrumentCard.cs |
| one knob each | un potentiomètre chacun | FluxInstrumentCard.cs |
| Open GUI ↗ | Ouvrir l’interface ↗ | GenericInstrumentCard.cs |
| Blend the sample itself (dry) with the grain cloud (wet) | Mélange l’échantillon lui-même (sec) avec le nuage de grains (traité) | GrainInstrumentCard.cs |
| Drag to move the read position · ⌥-click to type it · double-click to reset | Glisser pour déplacer la position de lecture · ⌥-clic pour la saisir · double-clic pour la réinitialiser | GrainInstrumentCard.cs |
| Root a semitone down | Fondamentale un demi-ton plus bas | GrainInstrumentCard.cs |
| Root a semitone up | Fondamentale un demi-ton plus haut | GrainInstrumentCard.cs |
| Root {0} plays the sample as recorded · coarse 0 · fine 0 | La fondamentale {0} joue l’échantillon tel qu’enregistré · coarse 0 · fine 0 | GrainInstrumentCard.cs |
| Scan moves through the file · Freeze holds Position · Key moves it with the note | Scan parcourt le fichier · Freeze maintient Position · Key la déplace avec la note | GrainInstrumentCard.cs |
| The root — the key that plays the sample as recorded. Scroll to step it · double-click takes it from the file name (C4 if none) | La fondamentale — la touche qui joue l’échantillon tel qu’enregistré. Défiler pour la changer · double-clic la prend dans le nom du fichier (C4 à défaut) | GrainInstrumentCard.cs |
| {0} {1}{2} · {3} VOICES · {4} GRAINS | {0} {1}{2} · {3} voix · {4} grains | GrainInstrumentCard.cs |
| {0} · position · pitch · pan | {0} · position · hauteur · panoramique | GrainInstrumentCard.cs |
| 2 lines · BBD | 2 lignes · BBD | KeysInstrumentCard.cs |
| Sustain pedal — click to hold it down; a keyboard's CC64 works too | Pédale de sustain — cliquer pour la maintenir enfoncée ; le CC64 d’un clavier fonctionne aussi | KeysInstrumentCard.cs |
| {0} / {1} voices | {0} / {1} voix | KeysInstrumentCard.cs |
| {0} held by the pedal | {0} tenues par la pédale | KeysInstrumentCard.cs |
| {0} · {1} voices | {0} · {1} voix | KeysInstrumentCard.cs |
| {0} ringing out | {0} en fin de résonance | KeysInstrumentCard.cs |
| Controllers · Osc Bank | Controllers · Osc Bank | MonolithInstrumentCard.cs |
| A new, empty layer — move zones into it from the rail | Une nouvelle couche vide — y déplacer des zones depuis le rail | MosaicInstrumentCard.cs |
| Blends the loop's end into its start (forward loops) | Fond la fin de la boucle dans son début (boucles avant) | MosaicInstrumentCard.cs |
| Click a key to make it the selected zone's root | Cliquer une touche pour en faire la fondamentale de la zone sélectionnée | MosaicInstrumentCard.cs |
| Detect from file name | Détecter d’après le nom du fichier | MosaicInstrumentCard.cs |
| Drag the nodes: attack, decay and sustain (up / down), release · Shift for fine steps · ⌥-click to type · double-click resets | Glisser les nœuds : attaque, déclin et maintien (haut / bas), relâchement · Maj pour les pas fins · ⌥-clic pour saisir · double-clic pour réinitialiser | MosaicInstrumentCard.cs |
| Drag: left / right for the cutoff, up / down for the resonance · Shift for fine steps · ⌥-click to type · double-click resets | Glisser : gauche / droite pour la coupure, haut / bas pour la résonance · Maj pour les pas fins · ⌥-clic pour saisir · double-clic pour réinitialiser | MosaicInstrumentCard.cs |
| Drop samples or a folder here · or pick a preset | Déposer des échantillons ou un dossier ici · ou choisir un préréglage | MosaicInstrumentCard.cs |
| Edit this zone's start, end and loop | Modifier le début, la fin et la boucle de cette zone | MosaicInstrumentCard.cs |
| Empty · drop samples or a folder, or pick a preset | Vide · déposer des échantillons ou un dossier, ou choisir un préréglage | MosaicInstrumentCard.cs |
| Exclusive is for hi-hats and valves: a new note chokes the off_by group. | Exclusive sert aux charleys et aux valves : une nouvelle note coupe le groupe off_by. | MosaicInstrumentCard.cs |
| Exclusive · group / off by | Exclusive · groupe / coupé par | MosaicInstrumentCard.cs |
| Filter off — the samples pass untouched | Filtre désactivé — les échantillons passent intacts | MosaicInstrumentCard.cs |
| Fine tune of the whole instrument, ±50 cents | Accord fin de tout l’instrument, ±50 cents | MosaicInstrumentCard.cs |
| How far the key moves the pitch — 100 % chromatic, 0 % every key plays the root | L’influence de la touche sur la hauteur — 100 % chromatique, 0 % toutes les touches jouent la fondamentale | MosaicInstrumentCard.cs |
| How fast a stolen or superseded voice fades | La vitesse d’extinction d’une voix volée ou remplacée | MosaicInstrumentCard.cs |
| How many strikes of one key may ring together | Le nombre de frappes d’une même touche qui peuvent sonner ensemble | MosaicInstrumentCard.cs |
| How much quieter a release sample gets the longer the key was held — drag up / down, ⌥-click to type | De combien l’échantillon de relâchement baisse quand la touche est tenue longtemps — glisser vers le haut / bas, ⌥-clic pour saisir | MosaicInstrumentCard.cs |
| How much velocity sets the level | Dans quelle mesure la vélocité règle le niveau | MosaicInstrumentCard.cs |
| Let the amp envelope open (or close) the filter | Laisser l’enveloppe d’ampli ouvrir (ou fermer) le filtre | MosaicInstrumentCard.cs |
| Loading · {0} / {1} · notes muted | Chargement · {0} / {1} · notes coupées | MosaicInstrumentCard.cs |
| MPE pressure (aftertouch) → level | Pression MPE (aftertouch) → niveau | MosaicInstrumentCard.cs |
| MPE slide (CC74) → cutoff | Glissement MPE (CC74) → coupure | MosaicInstrumentCard.cs |
| Make the note in the zone's file name its root | Prendre la note du nom de fichier de la zone comme fondamentale | MosaicInstrumentCard.cs |
| Move Start and End to where the sound begins and fades out (−48 dB) | Placer Start et End là où le son commence et s’éteint (−48 dB) | MosaicInstrumentCard.cs |
| Move Start, End and the loop to the nearest zero crossing | Placer Start, End et la boucle au passage par zéro le plus proche | MosaicInstrumentCard.cs |
| None of its {0} samples were found. Zone settings are kept — point Mosaic at the folder that holds them. | Aucun de ses {0} échantillons n’a été trouvé. Les réglages de zones sont conservés — indiquer à Mosaic le dossier qui les contient. | MosaicInstrumentCard.cs |
| Notes are silent until loading finishes | Les notes restent muettes jusqu’à la fin du chargement | MosaicInstrumentCard.cs |
| Off plays once · Fwd loops · Ping bounces — this zone only | Off joue une fois · Fwd boucle · Ping fait des allers-retours — cette zone uniquement | MosaicInstrumentCard.cs |
| Opens the mapping preview. A single file replaces the sample, as before. | Ouvre l’aperçu de l’assignation. Un fichier seul remplace l’échantillon, comme avant. | MosaicInstrumentCard.cs |
| Pack “{0}” is not installed | Le pack « {0} » n’est pas installé | MosaicInstrumentCard.cs |
| Pick the folder that holds the samples — Mosaic finds each file in it by its path or its name | Choisir le dossier qui contient les échantillons — Mosaic y retrouve chaque fichier par son chemin ou son nom | MosaicInstrumentCard.cs |
| Poly: chords (click again for the voice settings) · Mono: one at a time, legato with Glide · Choke: a new note cuts the ringing ones | Poly : accords (cliquer à nouveau pour les réglages de voix) · Mono : une à la fois, legato avec Glide · Choke : une nouvelle note coupe celles qui sonnent | MosaicInstrumentCard.cs |
| Release to build a multisample | Relâcher pour créer un multi-échantillon | MosaicInstrumentCard.cs |
| Release to load the sample | Relâcher pour charger l’échantillon | MosaicInstrumentCard.cs |
| Round-robin steps: the group's zones take turns 1 … N (each zone's step is set by its file) | Étapes de round-robin : les zones du groupe jouent à tour de rôle de 1 à N (l’étape de chaque zone est définie par son fichier) | MosaicInstrumentCard.cs |
| Samples are not copied into the project; the program keeps where they are. | Les échantillons ne sont pas copiés dans le projet ; le programme retient où ils se trouvent. | MosaicInstrumentCard.cs |
| Samples unavailable · notes muted | Échantillons indisponibles · notes coupées | MosaicInstrumentCard.cs |
| Settings → Downloads → Sample packs | Réglages → Téléchargements → Packs d’échantillons | MosaicInstrumentCard.cs |
| Shift every note, ±24 semitones | Décaler toutes les notes, ±24 demi-tons | MosaicInstrumentCard.cs |
| Slide from the last note — in Mono, legato | Glisser depuis la dernière note — en Mono, legato | MosaicInstrumentCard.cs |
| Stop loading — the instrument empties (undo brings it back) | Arrêter le chargement — l’instrument se vide (annuler le rétablit) | MosaicInstrumentCard.cs |
| The articulation — the SFZ's controller value its zones were chosen by | L’articulation — la valeur de contrôleur du SFZ selon laquelle ses zones ont été choisies | MosaicInstrumentCard.cs |
| The cutoff follows the key | La coupure suit la touche | MosaicInstrumentCard.cs |
| The level after everything | Le niveau après tout le reste | MosaicInstrumentCard.cs |
| The pitch wheel's range (MPE bends are per note) | La plage de la molette de hauteur (les bends MPE sont par note) | MosaicInstrumentCard.cs |
| The release samples' level | Le niveau des échantillons de relâchement | MosaicInstrumentCard.cs |
| The samples aren't where the program says | Les échantillons ne sont pas là où le programme l’indique | MosaicInstrumentCard.cs |
| The sustain pedal: a keyboard's CC64 holds released keys — click to stop listening to it | La pédale de sustain : le CC64 d’un clavier maintient les touches relâchées — cliquer pour ne plus l’écouter | MosaicInstrumentCard.cs |
| The zone's fine tune — drag, wheel, double-click resets | L’accord fin de la zone — glisser, molette, double-clic pour réinitialiser | MosaicInstrumentCard.cs |
| The zone's fine tune, ±100 cents | L’accord fin de la zone, ±100 cents | MosaicInstrumentCard.cs |
| The zone's root — drag or scroll | La fondamentale de la zone — glisser ou défiler | MosaicInstrumentCard.cs |
| This group's exclusive number | Le numéro exclusif de ce groupe | MosaicInstrumentCard.cs |
| This preset uses {0} samples from the pack. Zone settings are kept, no sound until installed. | Ce préréglage utilise {0} échantillons du pack. Les réglages de zones sont conservés, mais aucun son tant que le pack n’est pas installé. | MosaicInstrumentCard.cs |
| Velocity → level curve | Courbe vélocité → niveau | MosaicInstrumentCard.cs |
| Velocity → level: drag up for soft (quiet notes louder), down for hard; ⌥-click to type | Vélocité → niveau : glisser vers le haut pour doux (notes faibles plus fortes), vers le bas pour dur ; ⌥-clic pour saisir | MosaicInstrumentCard.cs |
| Voice stealing · order | Vol de voix · ordre | MosaicInstrumentCard.cs |
| Voices of this group stop when a note of that exclusive group starts | Les voix de ce groupe s’arrêtent quand une note de ce groupe exclusif démarre | MosaicInstrumentCard.cs |
| Zones can already be selected and edited | Les zones peuvent déjà être sélectionnées et modifiées | MosaicInstrumentCard.cs |
| a folder of samples | un dossier d’échantillons | MosaicInstrumentCard.cs |
| {0} / {1} · {2} / {3} files | {0} / {1} · {2} / {3} fichiers | MosaicInstrumentCard.cs |
| {0} busy · {1} on pedal · {2} release | {0} occupées · {1} sur pédale · {2} en relâchement | MosaicInstrumentCard.cs |
| {0} files | {0} fichier / {0} fichiers | MosaicInstrumentCard.cs |
| {0} groups · {1} · {2} | {0} groupes · {1} · {2} | MosaicInstrumentCard.cs |
| {0} instruments | {0} instrument / {0} instruments | MosaicInstrumentCard.cs |
| {0} layers | {0} couche / {0} couches | MosaicInstrumentCard.cs |
| {0} zones | {0} zone / {0} zones | MosaicInstrumentCard.cs |
| {0} · {1} found by name: {2} | {0} · {1} trouvés par le nom : {2} | MosaicInstrumentCard.cs |
| {0} · {1} zones | {0} · {1} zones | MosaicInstrumentCard.cs |
| Locate the samples | Localiser les échantillons | MosaicInstrumentCard.cs |
| Old voice fade | Old voice fade | MosaicInstrumentCard.cs |
| Open in Sample | Ouvrir dans Sample | MosaicInstrumentCard.cs |
| Sample not loaded | Échantillon non chargé | MosaicInstrumentCard.cs |
| Select a zone | Sélectionner une zone | MosaicInstrumentCard.cs |
| Snap to zero | Caler sur le zéro | MosaicInstrumentCard.cs |
| The zone's level | Le niveau de la zone | MosaicInstrumentCard.cs |
| The zone's pan | Le panoramique de la zone | MosaicInstrumentCard.cs |
| This zone's level | Le niveau de cette zone | MosaicInstrumentCard.cs |
| Total in RAM | Total en RAM | MosaicInstrumentCard.cs |
| Install pack | Installer le pack | MosaicInstrumentCard.cs |
| Locate folder… | Localiser le dossier… | MosaicInstrumentCard.cs |
| Trim silence | Rogner le silence | MosaicInstrumentCard.cs |
|  · {0} missing |  · {0} manquant(s) | MosaicInstrumentCard.cs |
| Empty | Vide | MosaicInstrumentCard.cs |
| Group · {0} | Groupe · {0} | MosaicInstrumentCard.cs |
| Loading {0} | Chargement {0} | MosaicInstrumentCard.cs |
| Loading {0} % | Chargement {0} % | MosaicInstrumentCard.cs |
| Vel → FM  off | Vél. → FM  off | OperatorInstrumentCard.cs |
| 11 topologies · feedback on A | 11 topologies · feedback sur A | OperatorInstrumentCard.cs |
| Detune of the chorus partner inside each voice | Désaccord du partenaire de chorus dans chaque voix | PendulumInstrumentCard.cs |
| Fan the notes across the stereo field by where the ball is | Répartir les notes dans le champ stéréo selon la position de la balle | PendulumInstrumentCard.cs |
| Free: seconds per swing at +100 % | Free : secondes par oscillation à +100 % | PendulumInstrumentCard.cs |
| How far the balls' rates drift apart — each ball a little faster than the one before | L’écart croissant entre les vitesses des balles — chaque balle un peu plus rapide que la précédente | PendulumInstrumentCard.cs |
| How long one swing (low → high → low) takes at +100 % | La durée d’une oscillation (bas → haut → bas) à +100 % | PendulumInstrumentCard.cs |
| Humanize on / off — the amount is the HUMAN slider | Humanize activé / désactivé — la quantité est réglée par le curseur HUMAN | PendulumInstrumentCard.cs |
| Latch the chord — it keeps swinging after you let go; the next chord replaces it | Latch de l’accord — il continue d’osciller après le relâchement ; l’accord suivant le remplace | PendulumInstrumentCard.cs |
| Level and pitch jitter per note | Variation aléatoire du niveau et de la hauteur par note | PendulumInstrumentCard.cs |
| One ball fewer (2 at least) | Une balle de moins (2 au minimum) | PendulumInstrumentCard.cs |
| One ball more (6 at most) | Une balle de plus (6 au maximum) | PendulumInstrumentCard.cs |
| One lane per ball: x is pitch, low to high. Crossing a chord degree plays that note; teal shows the wall ahead | Une voie par balle : x est la hauteur, du grave à l’aigu. Franchir un degré d’accord joue cette note ; le turquoise montre le mur devant | PendulumInstrumentCard.cs |
| Quantize: a crossing waits for the next 1/16 or 1/8 before it plays | Quantize : un franchissement attend la prochaine double-croche ou croche avant de jouer | PendulumInstrumentCard.cs |
| Reset spread, humanize and scale to their defaults | Réinitialiser spread, humanize et scale à leurs valeurs par défaut | PendulumInstrumentCard.cs |
| Restart every ball from its start when a new chord begins | Relancer chaque balle depuis son départ quand un nouvel accord commence | PendulumInstrumentCard.cs |
| Restart every ball's swing now | Relancer maintenant l’oscillation de chaque balle | PendulumInstrumentCard.cs |
| Snap the generated notes to a scale | Caler les notes générées sur une gamme | PendulumInstrumentCard.cs |
| Swing speed — centre stops the balls, left of centre runs them in reverse | Vitesse d’oscillation — le centre arrête les balles, à gauche du centre elles repartent en sens inverse | PendulumInstrumentCard.cs |
| Sync: a swing takes a note division of the tempo · Free: it takes a time in seconds | Sync : une oscillation dure une valeur de note du tempo · Free : elle dure un temps en secondes | PendulumInstrumentCard.cs |
| The bar: generated notes land on the 1/16 grid — this bar in neutral ink with the note just played in accent, the last bar dimmer ahead of the playhead | La mesure : les notes générées tombent sur la grille de doubles-croches — cette mesure en neutre avec la note qui vient de jouer en accent, la mesure précédente plus pâle devant la tête de lecture | PendulumInstrumentCard.cs |
| The swing curve — Pendulum slows at the walls, Linear keeps one speed, Bounce settles with a ripple | La courbe d’oscillation — Pendulum ralentit aux murs, Linear garde une vitesse constante, Bounce se stabilise avec une ondulation | PendulumInstrumentCard.cs |
| The voice: soft Keys, bright Glass, Saw, hollow Square or inharmonic Bell | La voix : Keys doux, Glass brillant, Saw, Square creux ou Bell inharmonique | PendulumInstrumentCard.cs |
| Up: the low wall plays the chord's lowest note · Down: its highest | Haut : le mur bas joue la note la plus basse de l’accord · Bas : la plus haute | PendulumInstrumentCard.cs |
| last note {0} · {1} | dernière note {0} · {1} | PendulumInstrumentCard.cs |
| hold a chord | tenir un accord | PendulumInstrumentCard.cs |
| POLY MOD — SOURCE → DESTINATION | Poly mod — source → destination | PentadInstrumentCard.cs |
| Re-roll the vintage seed (new per-voice spread) | Retirer la graine vintage (nouvelle dispersion par voix) | PentadInstrumentCard.cs |
| Restore lows at high Q | Restaurer les graves à Q élevé | PentadInstrumentCard.cs |
| SHAPE · PULSE WIDTH | Shape · pulse width | PentadInstrumentCard.cs |
| Voice setup: polyphony, allocation, glide mode, oversampling, vintage seed | Configuration des voix : polyphonie, allocation, mode de glide, suréchantillonnage, graine vintage | PentadInstrumentCard.cs |
| WHEEL MOD — DESTINATIONS | Wheel mod — destinations | PentadInstrumentCard.cs |
| per voice · {0} active routes | par voix · {0} route active / par voix · {0} routes actives | PentadInstrumentCard.cs |
| {0:0.#} kHz · ×{1} OS · CPU {2:0} % | {0:0.#} kHz · ×{1} OS · CPU {2:0} % | PentadInstrumentCard.cs |
| 1→2: resonator 1 rings into resonator 2 · 1+2: both struck, mixed | 1→2 : le résonateur 1 excite le résonateur 2 · 1+2 : les deux sont frappés, mélangés | PhysicalInstrumentCard.cs |
| How hard a released key damps the body — at 0 it rings on, fully up it stops at once | La force avec laquelle une touche relâchée étouffe le corps — à 0 il continue de sonner, au maximum il s’arrête aussitôt | PhysicalInstrumentCard.cs |
| Partials as the engine tunes them — drag ↔ to spread the series (Ratio), ↕ to tilt the highs (Bright) | Partiels tels que le moteur les accorde — glisser ↔ pour étaler la série (Ratio), ↕ pour incliner les aigus (Bright) | PhysicalInstrumentCard.cs |
| Poly: 8 voices ring over each other · Mono: a new strike chokes the sounding note | Poly : 8 voix qui se chevauchent · Mono : une nouvelle frappe coupe la note qui sonne | PhysicalInstrumentCard.cs |
| Res 1 against Res 2 — both at full level in the middle; in 1→2, res 1's own sound against the body it rings | Res 1 face à Res 2 — les deux à plein niveau au milieu ; en 1→2, le son propre de res 1 face au corps qu’il fait résonner | PhysicalInstrumentCard.cs |
| Resonator 2 on / off — resonator 1 always rings | Résonateur 2 activé / désactivé — le résonateur 1 sonne toujours | PhysicalInstrumentCard.cs |
| Stiffness sets the contact time: soft = long and dark, hard = short and bright | Stiffness règle le temps de contact : doux = long et sombre, dur = court et brillant | PhysicalInstrumentCard.cs |
| Which resonator the controls below edit | Le résonateur que modifient les commandes ci-dessous | PhysicalInstrumentCard.cs |
| env → freq off | env → freq off | PhysicalInstrumentCard.cs |
| Add a receiver to {0}: a voice param, a kit setting or a voice's effect | Ajouter un récepteur à {0} : un paramètre de voix, un réglage du kit ou l’effet d’une voix | RhythmInstrumentCard.cs |
| Amp decay — shortens the hit | Amp decay — raccourcit le coup | RhythmInstrumentCard.cs |
| Attack click / snap | Clic / claquement d’attaque | RhythmInstrumentCard.cs |
| Bank {0} — play and edit this pattern · right-click to copy or clear it | Banque {0} — jouer et modifier ce motif · clic droit pour le copier ou l’effacer | RhythmInstrumentCard.cs |
| Clear its steps in bank {0} | Effacer ses pas dans la banque {0} | RhythmInstrumentCard.cs |
| Drag the Start and End lines to set them · ⌥-click one to type it · double-click to load another file | Glisser les lignes Start et End pour les régler · ⌥-clic sur l’une pour la saisir · double-clic pour charger un autre fichier | RhythmInstrumentCard.cs |
| Every {0} ({1}) | Chaque {0} ({1}) | RhythmInstrumentCard.cs |
| How much louder an accented step plays | De combien un pas accentué joue plus fort | RhythmInstrumentCard.cs |
| How much of the file plays, from Start | La part du fichier jouée, à partir de Start | RhythmInstrumentCard.cs |
| Humanize: a random nudge to every hit's velocity | Humanize : un léger décalage aléatoire de la vélocité de chaque frappe | RhythmInstrumentCard.cs |
| Load a one-shot into this voice (or drop a file on the voice) | Charger un one-shot dans cette voix (ou déposer un fichier sur la voix) | RhythmInstrumentCard.cs |
| Load a sample into {0} | Charger un échantillon dans {0} | RhythmInstrumentCard.cs |
| Pitch (Kick, Snare, Tom, Hats, Perc) or noise colour (Clap, Rim) | Hauteur (Kick, Snare, Tom, Hats, Perc) ou couleur du bruit (Clap, Rim) | RhythmInstrumentCard.cs |
| Play the loaded sample | Jouer l’échantillon chargé | RhythmInstrumentCard.cs |
| Play the region back to front | Jouer la région à l’envers | RhythmInstrumentCard.cs |
| Play the synth engine | Jouer le moteur de synthèse | RhythmInstrumentCard.cs |
| Snare body ↔ noise · Hats metal ↔ noise · Perc ring ratio | Snare corps ↔ bruit · Hats métal ↔ bruit · Perc rapport d’anneau | RhythmInstrumentCard.cs |
| Sound: the voice's source and shape · FX: its own effects, before the kit bus | Sound : la source et la forme de la voix · FX : ses propres effets, avant le bus du kit | RhythmInstrumentCard.cs |
| Swing: delays every second 16th — at 100 % by half a step | Swing : retarde une double-croche sur deux — à 100 %, d’un demi-pas | RhythmInstrumentCard.cs |
| Synth: the voice's own engine · Sample: a loaded one-shot (loads one if the voice has none) | Synth : le moteur propre de la voix · Sample : un one-shot chargé (en charge un si la voix n’en a pas) | RhythmInstrumentCard.cs |
| The kit's eight macros — knobs over every voice and its effects | Les huit macros du kit — des potentiomètres sur chaque voix et ses effets | RhythmInstrumentCard.cs |
| The kit's output level | Le niveau de sortie du kit | RhythmInstrumentCard.cs |
| The selected voice's sound and effects | Le son et les effets de la voix sélectionnée | RhythmInstrumentCard.cs |
| The voice's level in the kit | Le niveau de la voix dans le kit | RhythmInstrumentCard.cs |
| Where in the file the hit starts | L’endroit du fichier où commence la frappe | RhythmInstrumentCard.cs |
| {0} of {1} · 1/16 · bank {2} | {0} sur {1} · 1/16 · banque {2} | RhythmInstrumentCard.cs |
| {0} slots · {1} assigned | {0} emplacements · {1} assignés | RhythmInstrumentCard.cs |
| {0} voices | {0} voix / {0} voix | RhythmInstrumentCard.cs |
| {0} · MIDI {1}⏎ Click: select and play · drop a file to load it · right-click for more | {0} · MIDI {1}⏎ Clic : sélectionner et jouer · déposer un fichier pour le charger · clic droit pour plus d’options | RhythmInstrumentCard.cs |
| {0} — click to load another file | {0} — cliquer pour charger un autre fichier | RhythmInstrumentCard.cs |
| Fill every 16th | Remplir toutes les doubles-croches | RhythmInstrumentCard.cs |
| Fill every 8th | Remplir toutes les croches | RhythmInstrumentCard.cs |
| Fill every beat | Remplir tous les temps | RhythmInstrumentCard.cs |
| Transpose, ±12 semitones | Transposition, ±12 demi-tons | RhythmInstrumentCard.cs |
| shift+step = accent | Maj+pas = accent | RhythmInstrumentCard.cs |
| Glue: a bus compressor on the whole kit — holds the hits together (0 = off) | Glue : un compresseur de bus sur tout le kit — soude les frappes entre elles (0 = désactivé) | RhythmInstrumentCard.cs |
| The hit: amp contour over the pitch drop · drag across for Decay, up / down for Tune (Shift = fine) · ⌥-click to type them | La frappe : contour d’amplitude sur la chute de hauteur · glisser latéralement pour Decay, vers le haut / bas pour Tune (Maj = fin) · ⌥-clic pour les saisir | RhythmInstrumentCard.cs |
| Load sample… | Charger un échantillon… | RhythmInstrumentCard.cs |
| Off-beats | Contretemps | RhythmInstrumentCard.cs |
| Shift left | Décaler à gauche | RhythmInstrumentCard.cs |
| Shift right | Décaler à droite | RhythmInstrumentCard.cs |
| All {0} voices | Toutes les {0} voix / Toutes les {0} voix | RhythmInstrumentCard.cs |
| Bank {0} | Banque {0} | RhythmInstrumentCard.cs |
| Clear bank {0} | Effacer la banque {0} | RhythmInstrumentCard.cs |
| Copy {0} to | Copier {0} vers | RhythmInstrumentCard.cs |
| Step {0} accent | Pas {0} accentué | RhythmInstrumentCard.cs |
| Step {0} quiet | Pas {0} faible | RhythmInstrumentCard.cs |
| "{0}" names {1} — make it the root | « {0} » désigne {1} — en faire la fondamentale | SamplerInstrumentCard.cs |
| Click a key to make it the root — the key that plays the sample as recorded | Cliquer une touche pour en faire la fondamentale — celle qui joue l’échantillon tel qu’enregistré | SamplerInstrumentCard.cs |
| Drag Start / End (and the loop markers when looping) · ⌥-click a marker to type it · double-click to reset it | Glisser Start / End (et les repères de boucle en mode boucle) · ⌥-clic sur un repère pour le saisir · double-clic pour le réinitialiser | SamplerInstrumentCard.cs |
| Drag a sample from the Files tab or the browser | Glisser un échantillon depuis l’onglet Fichiers ou le navigateur | SamplerInstrumentCard.cs |
| Drop a sample here | Déposer un échantillon ici | SamplerInstrumentCard.cs |
| Env {0} — {1} | Env {0} — {1} | SamplerInstrumentCard.cs |
| Filter off — the sample passes untouched | Filtre désactivé — l’échantillon passe intact | SamplerInstrumentCard.cs |
| Fine tune, ±50 cents | Accord fin, ±50 cents | SamplerInstrumentCard.cs |
| How far the envelope moves the cutoff, ±6 octaves | L’amplitude du déplacement de la coupure par l’enveloppe, ±6 octaves | SamplerInstrumentCard.cs |
| Let the amp envelope open (or close) the filter — the depth is ENV | Laisser l’enveloppe d’ampli ouvrir (ou fermer) le filtre — la profondeur est ENV | SamplerInstrumentCard.cs |
| Move Start, End and the loop to the nearest zero crossing — no clicks | Placer Start, End et la boucle au passage par zéro le plus proche — sans clics | SamplerInstrumentCard.cs |
| No sample · drop one from the Files tab or the browser | Aucun échantillon · en déposer un depuis l’onglet Fichiers ou le navigateur | SamplerInstrumentCard.cs |
| Off plays once · Fwd loops · Ping bounces · Rev loops backwards | Off joue une fois · Fwd boucle · Ping fait des allers-retours · Rev boucle à l’envers | SamplerInstrumentCard.cs |
| Off · low-pass · high-pass · band-pass (12 dB/oct) | Off · passe-bas · passe-haut · passe-bande (12 dB/oct) | SamplerInstrumentCard.cs |
| Poly: 16 voices · Mono: one at a time (legato with Glide) · Choke: a new note cuts the ringing ones | Poly : 16 voix · Mono : une à la fois (legato avec Glide) · Choke : une nouvelle note coupe celles qui sonnent | SamplerInstrumentCard.cs |
| Slide from the last note — in Mono, legato: no retrigger | Glisser depuis la dernière note — en Mono, legato : sans redéclenchement | SamplerInstrumentCard.cs |
| The cutoff follows the key, 0 … 100 % | La coupure suit la touche, 0 … 100 % | SamplerInstrumentCard.cs |
| The root note — scroll to step it | La note fondamentale — défiler pour la changer | SamplerInstrumentCard.cs |
| The sample's file name isn't known | Le nom de fichier de l’échantillon est inconnu | SamplerInstrumentCard.cs |
| The sample's level before the envelope | Le niveau de l’échantillon avant l’enveloppe | SamplerInstrumentCard.cs |
| Velocity sets the level — the amount is the rail's VEL → VOL | La vélocité règle le niveau — la quantité est le VEL → VOL du rail | SamplerInstrumentCard.cs |
| a long tail after release | une longue queue après le relâchement | SamplerInstrumentCard.cs |
| almost a gate — the sample is heard whole | presque un gate — l’échantillon s’entend en entier | SamplerInstrumentCard.cs |
| every key plays the same pitch | toutes les touches jouent la même hauteur | SamplerInstrumentCard.cs |
| held at the sustain level | maintenu au niveau de sustain | SamplerInstrumentCard.cs |
| the key sets the playback speed | la touche règle la vitesse de lecture | SamplerInstrumentCard.cs |
| the sample plays at its own pitch on the root | l’échantillon joue à sa propre hauteur sur la fondamentale | SamplerInstrumentCard.cs |
| {0} plays the sample at its own pitch | {0} joue l’échantillon à sa propre hauteur | SamplerInstrumentCard.cs |
| {0} · no crossfade | {0} · sans fondu enchaîné | SamplerInstrumentCard.cs |
| The voices' level | Le niveau des voix | SamplerInstrumentCard.cs |
| a slow swell | une montée lente | SamplerInstrumentCard.cs |
| Filter off — the oscillator goes straight to the amplifier | Filtre désactivé — l’oscillateur va directement à l’amplificateur | SynthInstrumentCard.cs |
| Vel → Vol  off | Vél. → Vol  off | SynthInstrumentCard.cs |
| 8 slots · {0} assigned | 8 emplacements · {0} assignés | VoltInstrumentCard.cs |
| Macro {0} · {1} | Macro {0} · {1} | VoltInstrumentCard.cs |
| vibrato always rides the pitch | le vibrato suit toujours la hauteur | VoltInstrumentCard.cs |
| Hold: keep arpeggiating after the keys are released | Hold : continuer l’arpège après le relâchement des touches | ArpMidiBody.cs |
| MIDI out · ch 1 | Sortie MIDI · can. 1 | ArpMidiBody.cs |
| MIDI · waiting for notes | MIDI · en attente de notes | ArpMidiBody.cs |
| Restart the pattern from step 1 | Relancer le motif depuis le pas 1 | ArpMidiBody.cs |
| step {0} · {1} | pas {0} · {1} | ArpMidiBody.cs |
| {0} notes · {1} oct · cycle {2} steps | {0} notes · {1} oct · cycle de {2} pas | ArpMidiBody.cs |
| {0} · {1} oct | {0} · {1} oct | ArpMidiBody.cs |
| Next Groove lane | Voie de groove suivante | ArpMidiBody.cs |
| no notes yet | aucune note pour l’instant | ArpMidiBody.cs |
| Custom: your own shifts | Custom : vos propres décalages | ChordMidiBody.cs |
| Drop added notes outside the scale a semitone into it | Ramener d’un demi-ton dans la gamme les notes ajoutées hors de celle-ci | ChordMidiBody.cs |
| Drop added notes outside {0} a semitone into it | Ramener d’un demi-ton dans {0} les notes ajoutées hors de celle-ci | ChordMidiBody.cs |
| Fold key — click for the next, Shift-click for the previous | Fold key — cliquer pour la suivante, Maj-clic pour la précédente | ChordMidiBody.cs |
| Fold scale — major / natural minor | Fold scale — majeure / mineure naturelle | ChordMidiBody.cs |
| In {0} · {1} · {2} voices | En {0} · {1} · {2} voix | ChordMidiBody.cs |
| In {0} · {1} · {2} voices · strum {3} ms · spread {4} | En {0} · {1} · {2} voix · strum {3} ms · spread {4} | ChordMidiBody.cs |
| MIDI · latency 0 smp | MIDI · latence 0 éch. | ChordMidiBody.cs |
| Play the note you pressed as well as the added notes | Jouer la note appuyée en plus des notes ajoutées | ChordMidiBody.cs |
| Shift {0} on / off | Décalage {0} activé / désactivé | ChordMidiBody.cs |
| Shift {0} velocity offset — drag sideways, ⌥-click to type, double-click resets | Décalage de vélocité {0} — glisser latéralement, ⌥-clic pour saisir, double-clic pour réinitialiser | ChordMidiBody.cs |
| Shift {0}: drag up / down (±12 semitones) · ⌥-click to type · double-click resets | Décalage {0} : glisser vers le haut / bas (±12 demi-tons) · ⌥-clic pour saisir · double-clic pour réinitialiser | ChordMidiBody.cs |
| {0}: set the shifts to {1} | {0} : régler les décalages sur {1} | ChordMidiBody.cs |
| 1 bar at {0:0} BPM · {1} notes | 1 mesure à {0:0} BPM · {1} notes | LengthMidiBody.cs |
| Count the length from the note-on or the note-off — click to switch | Compter la durée depuis le note-on ou le note-off — cliquer pour basculer | LengthMidiBody.cs |
| Count the length from the note-on, or from the note-off (the note fires when the key is released) | Compter la durée depuis le note-on, ou depuis le note-off (la note part au relâchement de la touche) | LengthMidiBody.cs |
| Each note lasts until the next one starts (at most to the bar line) | Chaque note dure jusqu’au début de la suivante (au plus jusqu’à la barre de mesure) | LengthMidiBody.cs |
| Legato · retrigger held | Legato · redéclenchement des notes tenues | LengthMidiBody.cs |
| Length mode: Sync / ms / Gate % — click for the next, Shift-click for the previous | Mode de durée : Sync / ms / Gate % — cliquer pour le suivant, Maj-clic pour le précédent | LengthMidiBody.cs |
| Length — drag up / down, double-click resets | Length — glisser vers le haut / bas, double-clic pour réinitialiser | LengthMidiBody.cs |
| No note crosses the bar line it started in | Aucune note ne franchit la barre de mesure où elle a commencé | LengthMidiBody.cs |
| Outline — in · fill — out · band — Random's range | Contour — entrée · aplat — sortie · bande — plage de Random | LengthMidiBody.cs |
| Outline — the note played in · fill — the note out · band — the range Random spreads it over. The shaded part is past the bar line. | Contour — la note jouée en entrée · aplat — la note en sortie · bande — la plage sur laquelle Random la répartit. La partie ombrée dépasse la barre de mesure. | LengthMidiBody.cs |
| Sync: a tempo division · ms: a fixed time · Gate %: a share of how long each note was held | Sync : une division du tempo · ms : un temps fixe · Gate % : une part de la durée de tenue de chaque note | LengthMidiBody.cs |
| loud notes longer at + | notes fortes plus longues à + | LengthMidiBody.cs |
| low notes longer at + | notes graves plus longues à + | LengthMidiBody.cs |
| {0} {1} · from {2} · avg out {3} | {0} {1} · depuis {2} · sortie moy. {3} | LengthMidiBody.cs |
| {0} — drag up / down, double-click resets | {0} — glisser vers le haut / bas, double-clic pour réinitialiser | LengthMidiBody.cs |
| Clip length limit | Limite de durée du clip | LengthMidiBody.cs |
| spread per note | dispersion par note | LengthMidiBody.cs |
| Card size: S (mini) / L (full) | Taille de la carte : S (mini) / L (complète) | MidiCardHeader.cs |
| Map a hardware control: arm, touch a control on the card, move a knob or key | Assigner une commande matérielle : armer, toucher une commande sur la carte, bouger un potentiomètre ou une touche | MidiCardHeader.cs |
| Chance that a varied note is dropped | Probabilité qu’une note variée soit supprimée | RandomMidiBody.cs |
| Chance — how many notes get varied at all | Chance — combien de notes sont variées | RandomMidiBody.cs |
| Dashed — in · filled — out (brightness = velocity) · × — skipped | Pointillés — entrée · plein — sortie (luminosité = vélocité) · × — ignoré | RandomMidiBody.cs |
| Distribution: Gauss / Even / Walk — click for the next, Shift-click for the previous | Distribution : Gauss / Even / Walk — cliquer pour la suivante, Maj-clic pour la précédente | RandomMidiBody.cs |
| Distribution: Gauss — mostly small moves · Even — any amount equally · Walk — drifts from note to note | Distribution : Gauss — surtout de petits écarts · Even — toute amplitude de façon égale · Walk — dérive de note en note | RandomMidiBody.cs |
| Hold the roll showing now — it repeats every bar | Maintenir le roulement affiché — il se répète à chaque mesure | RandomMidiBody.cs |
| Jump up or down by up to two octaves | Saute vers le haut ou le bas jusqu’à deux octaves | RandomMidiBody.cs |
| Lock — hold the roll showing now | Lock — maintenir le roulement affiché | RandomMidiBody.cs |
| Next seed — a new roll | Graine suivante — un nouveau tirage | RandomMidiBody.cs |
| One bar of eight notes: dashed — the note in, filled — the note out (brightness = velocity, shifted right when delayed), × — skipped | Une mesure de huit notes : pointillés — la note en entrée, plein — la note en sortie (luminosité = vélocité, décalée à droite si retardée), × — ignorée | RandomMidiBody.cs |
| Per note — every note rolls its own values · Per bar — one set of values for the whole bar | Per note — chaque note tire ses propres valeurs · Per bar — un seul jeu de valeurs pour toute la mesure | RandomMidiBody.cs |
| Play late by up to this long (a live effect can't play early) | Jouer en retard jusqu’à cette durée (un effet en direct ne peut pas jouer en avance) | RandomMidiBody.cs |
| Push the velocity up or down by up to this much | Pousser la vélocité vers le haut ou le bas jusqu’à cette valeur | RandomMidiBody.cs |
| Reroll — the next seed | Reroll — la graine suivante | RandomMidiBody.cs |
| Snap every varied note into C major | Ramener chaque note variée dans Do majeur | RandomMidiBody.cs |
| Stay in scale · C maj | Rester dans la gamme · Do maj | RandomMidiBody.cs |
| The shape of the roll — 1500 draws from the same generator | La forme du roulement — 1500 tirages du même générateur | RandomMidiBody.cs |
| Transpose up or down by up to this many semitones | Transposer vers le haut ou le bas jusqu’à ce nombre de demi-tons | RandomMidiBody.cs |
| bar {0} · {1} changed · {2} skipped | mesure {0} · {1} modifiées · {2} ignorées | RandomMidiBody.cs |
| {0} changed · {1} skip | {0} modifiées · {1} ignorées | RandomMidiBody.cs |
| Clear the scale down to the root (Custom) | Réduire la gamme à la fondamentale (Custom) | ScaleMidiBody.cs |
| Click a note to add it to or take it out of the scale (the scale turns Custom) | Cliquer une note pour l’ajouter à la gamme ou l’en retirer (la gamme passe en Custom) | ScaleMidiBody.cs |
| Custom: your own notes — click the note map to edit | Custom : vos propres notes — cliquer la carte des notes pour modifier | ScaleMidiBody.cs |
| Fold — where an out-of-scale note goes: the nearest scale note (a tie goes up), the one below or the one above | Fold — où va une note hors gamme : la note de gamme la plus proche (à égalité, vers le haut), celle du dessous ou celle du dessus | ScaleMidiBody.cs |
| Follow key · from input | Follow key · depuis l’entrée | ScaleMidiBody.cs |
| Follow key — the root moves to the key that best fits what you play (keeping the scale type) | Follow key — la fondamentale se déplace vers la tonalité qui correspond le mieux à ce qui est joué (en gardant le type de gamme) | ScaleMidiBody.cs |
| LAST IN → OUT | Dernière entrée → sortie | ScaleMidiBody.cs |
| Learn — play the notes of your scale; they pass through untouched and become the Custom scale. Click again to finish. | Learn — jouer les notes de la gamme ; elles passent intactes et deviennent la gamme Custom. Cliquer à nouveau pour terminer. | ScaleMidiBody.cs |
| Root — click the arrows or scroll | Root — cliquer les flèches ou défiler | ScaleMidiBody.cs |
| The key in effect: root and scale | La tonalité en vigueur : fondamentale et gamme | ScaleMidiBody.cs |
| The last note played and what it came out as | La dernière note jouée et ce qu’elle est devenue | ScaleMidiBody.cs |
| empty · notes pass | vide · les notes passent | ScaleMidiBody.cs |
| {0} of 12 remapped | {0} sur 12 réassignées | ScaleMidiBody.cs |
| {0} {1} · {2} notes | {0} {1} · {2} notes | ScaleMidiBody.cs |
| {0} {1} · {2} notes · fold {3} ·  | {0} {1} · {2} notes · fold {3} ·  | ScaleMidiBody.cs |
| {0} — {1} notes | {0} — {1} notes | ScaleMidiBody.cs |
| {0} — ± an octave, drag or scroll the note for semitones, ⌥-click the note to type it, double-click resets. Notes outside the range pass through. | {0} — ± une octave, glisser ou défiler la note pour les demi-tons, ⌥-clic sur la note pour la saisir, double-clic pour réinitialiser. Les notes hors de la plage passent telles quelles. | ScaleMidiBody.cs |
| Compand — the same around the middle: above 1 stretches to the edges, below 1 squeezes to the middle | Compand — la même chose autour du milieu : au-dessus de 1, étire vers les bords ; en dessous de 1, resserre vers le milieu | VelocityMidiBody.cs |
| Curve — bend the response: Drive above 1 lifts soft notes, below 1 pushes them down | Curve — infléchit la réponse : Drive au-dessus de 1 remonte les notes douces, en dessous de 1 les abaisse | VelocityMidiBody.cs |
| Drag up / down, Shift for fine, double-click resets | Glisser vers le haut / bas, Maj pour les pas fins, double-clic pour réinitialiser | VelocityMidiBody.cs |
| Fixed — every note comes out at one velocity | Fixed — toutes les notes sortent à une seule vélocité | VelocityMidiBody.cs |
| Input across, output up · dashed — the Out range · band — Random · dots — the last notes | Entrée en abscisse, sortie en ordonnée · pointillés — la plage Out · bande — Random · points — les dernières notes | VelocityMidiBody.cs |
| Input velocity across, output up. Dashed — the Out range; the band — where Random can put a note; dots — the last notes played | Vélocité d’entrée en abscisse, de sortie en ordonnée. Pointillés — la plage Out ; la bande — où Random peut placer une note ; points — les dernières notes jouées | VelocityMidiBody.cs |
| Out range — drag a handle, ⌥-click to type it, double-click resets to 1–127 | Out range — glisser une poignée, ⌥-clic pour la saisir, double-clic pour revenir à 1–127 | VelocityMidiBody.cs |
| Random amount, ±0–64 velocity — drag up / down, double-click resets | Quantité aléatoire, ±0–64 de vélocité — glisser vers le haut / bas, double-clic pour réinitialiser | VelocityMidiBody.cs |
| Random both ways, only up, or only down | Aléatoire dans les deux sens, seulement vers le haut, ou seulement vers le bas | VelocityMidiBody.cs |
| Random — spread each note's velocity (the amount is kept while it is off) | Random — disperse la vélocité de chaque note (la quantité est conservée tant qu’il est désactivé) | VelocityMidiBody.cs |
| Random: both ways → up → down → off — click to step, Shift-click back | Random : deux sens → haut → bas → désactivé — cliquer pour avancer, Maj-clic pour reculer | VelocityMidiBody.cs |
| The last 12 notes: outline — velocity in, fill — velocity out | Les 12 dernières notes : contour — vélocité en entrée, aplat — vélocité en sortie | VelocityMidiBody.cs |
| Velocity mode: Curve / Compand / Fixed — click for the next, Shift-click for the previous | Mode de vélocité : Curve / Compand / Fixed — cliquer pour le suivant, Maj-clic pour le précédent | VelocityMidiBody.cs |
| every note at this velocity | toutes les notes à cette vélocité | VelocityMidiBody.cs |
| last {0} → {1} | dernière {0} → {1} | VelocityMidiBody.cs |
| squeezed to the middle | resserré vers le milieu | VelocityMidiBody.cs |
| stretched to the edges | étiré vers les bords | VelocityMidiBody.cs |
| soft notes louder | notes douces plus fortes | VelocityMidiBody.cs |
| soft notes quieter | notes douces plus faibles | VelocityMidiBody.cs |
| Close the plug-in's own window | Fermer la fenêtre propre au plug-in | PluginCard.cs |
| Couldn't open the {0} window. | Impossible d’ouvrir la fenêtre {0}. | PluginCard.cs |
| Couldn't open the {0} window: {1} | Impossible d’ouvrir la fenêtre {0} : {1} | PluginCard.cs |
| Open the plug-in's own window | Ouvrir la fenêtre propre au plug-in | PluginCard.cs |
| Save the plug-in's state as a preset | Enregistrer l’état du plug-in comme préréglage | PluginCard.cs |
| This plug-in exposes no parameters | Ce plug-in n’expose aucun paramètre | PluginCard.cs |
| No parameter matches | Aucun paramètre correspondant | PluginCard.cs |
| Open editor | Ouvrir l’éditeur | PluginCard.cs |
| Add a receiver: a pad control, the pad's instrument or one of its effects | Ajouter un récepteur : une commande de pad, l’instrument du pad ou l’un de ses effets | MacroPanel.cs |
| Macro {0} is free — right-click (or Map) to give it a target | La macro {0} est libre — clic droit (ou Map) pour lui donner une cible | MacroPanel.cs |
| Nothing mapped yet. Map gives this knob a pad control, an instrument param or any of the kit's effects. | Rien d’assigné pour l’instant. Map donne à ce potentiomètre une commande de pad, un paramètre d’instrument ou l’un des effets du kit. | MacroPanel.cs |
| Receivers and ranges… | Récepteurs et plages… | MacroPanel.cs |
| Remove every receiver of this macro | Retirer tous les récepteurs de cette macro | MacroPanel.cs |
| The range the macro sweeps: drag either end · the tick is where it is now | La plage balayée par la macro : glisser l’une ou l’autre extrémité · le repère indique la position actuelle | MacroPanel.cs |
| {0} — {1}⏎ Drag to turn · click to show its receivers · right-click to map, rename or clear | {0} — {1}⏎ Glisser pour tourner · cliquer pour voir ses récepteurs · clic droit pour assigner, renommer ou effacer | MacroPanel.cs |
| Remove this receiver | Retirer ce récepteur | MacroPanel.cs |
| every macro assigned | toutes les macros assignées | MacroPanel.cs |
| Map | Assigner | MacroPanel.cs |
| Map to | Assigner à | MacroPanel.cs |
| Clear {0} mappings | Effacer {0} assignation / Effacer {0} assignations | MacroPanel.cs |
| all | tous | MacroPanel.cs |
| all {0} | tous les {0} | MacroPanel.cs |
| not assigned | non assigné | MacroPanel.cs |
| {0} free | {0} libre(s) | MacroPanel.cs |
| {0} params | {0} param. / {0} param. | MacroPanel.cs |
| A MIDI Effect Rack holds up to {0} chains | Un MIDI Effect Rack contient jusqu’à {0} chaînes | RackCard.Chains.cs |
| A MIDI Effect Rack holds up to {0} chains. | Un MIDI Effect Rack contient jusqu’à {0} chaînes. | RackCard.Chains.cs |
| A MIDI Effect Rack takes MIDI effects only. | Un MIDI Effect Rack n’accepte que des effets MIDI. | RackCard.Chains.cs |
| A rack can't go inside a rack. | Un rack ne peut pas aller dans un rack. | RackCard.Chains.cs |
| Add a MIDI effect — or drop one from the Browser | Ajouter un effet MIDI — ou en déposer un depuis le Navigateur | RackCard.Chains.cs |
| Add a chain to start. | Ajouter une chaîne pour commencer. | RackCard.Chains.cs |
| Add a chain — or drop a device from the Browser on the list | Ajouter une chaîne — ou déposer un appareil du Navigateur sur la liste | RackCard.Chains.cs |
| Add an effect — or drop one from the Browser | Ajouter un effet — ou en déposer un depuis le Navigateur | RackCard.Chains.cs |
| An Audio Effect Rack takes audio effects only. | Un Audio Effect Rack n’accepte que des effets audio. | RackCard.Chains.cs |
| An Instrument Rack can't go inside a rack — add its instruments as chains. | Un Instrument Rack ne peut pas aller dans un rack — ajouter ses instruments comme chaînes. | RackCard.Chains.cs |
| Click to add or remove | Cliquer pour ajouter ou retirer | RackCard.Chains.cs |
| Controlled by {0} · click to open the macro | Contrôlé par {0} · cliquer pour ouvrir la macro | RackCard.Chains.cs |
| Drop a device here. | Déposer un appareil ici. | RackCard.Chains.cs |
| Open the full editor | Ouvrir l’éditeur complet | RackCard.Chains.cs |
| Pick an instrument — or drop one from the Browser | Choisir un instrument — ou en déposer un depuis le Navigateur | RackCard.Chains.cs |
| Plug-in presets can't go in a rack chain yet. | Les préréglages de plug-in ne peuvent pas encore aller dans une chaîne de rack. | RackCard.Chains.cs |
| This preset can't go in a rack. | Ce préréglage ne peut pas aller dans un rack. | RackCard.Chains.cs |
| This rack can't host plug-ins. | Ce rack ne peut pas accueillir de plug-ins. | RackCard.Chains.cs |
| {0} can't go in a rack chain. | {0} ne peut pas aller dans une chaîne de rack. | RackCard.Chains.cs |
| {0} · MIDI effect {1} | {0} · effet MIDI {1} | RackCard.Chains.cs |
| {0} · {1} left → right | {0} · {1} de gauche à droite | RackCard.Chains.cs |
| Delete chain | Supprimer la chaîne | RackCard.Chains.cs |
| Replace instrument | Remplacer l’instrument | RackCard.Chains.cs |
| Turn off | Désactiver | RackCard.Chains.cs |
| Turn on | Activer | RackCard.Chains.cs |
| Chain · {0} | Chaîne · {0} | RackCard.Chains.cs |
| Map to {0} | Assigner à {0} | RackCard.Chains.cs |
| Unmap from {0} | Désassigner de {0} | RackCard.Chains.cs |
| sel {0}–{1} | sél. {0}–{1} | RackCard.Chains.cs |
| MIDI effects run in the first 16 chains. | Les effets MIDI s’exécutent dans les 16 premières chaînes. | RackCard.Clipboard.cs |
| Nothing to paste here. | Rien à coller ici. | RackCard.Clipboard.cs |
| Pasted {0} devices, skipped {1} this chain can't take | {0} appareils collés, {1} ignorés car cette chaîne ne peut pas les accepter | RackCard.Clipboard.cs |
| The device can't go there. | L’appareil ne peut pas aller là. | RackCard.Clipboard.cs |
| This chain can't take the copied devices. | Cette chaîne ne peut pas accepter les appareils copiés. | RackCard.Clipboard.cs |
| This device can't be duplicated | Cet appareil ne peut pas être dupliqué | RackCard.Clipboard.cs |
| This rack's chains take no MIDI effects. | Les chaînes de ce rack n’acceptent pas d’effets MIDI. | RackCard.Clipboard.cs |
| This rack's chains take no audio effects. | Les chaînes de ce rack n’acceptent pas d’effets audio. | RackCard.Clipboard.cs |
| Nothing to paste | Rien à coller | RackCard.Clipboard.cs |
|  · sidechain dropped |  · sidechain supprimé | RackCard.Clipboard.cs |
| Copied {0} | {0} copié | RackCard.Clipboard.cs |
| Drag the ends · min above max inverts | Glisser les extrémités · min au-dessus de max inverse | RackCard.MacroTab.cs |
| Map this macro: click parameters on the Chains tab | Assigner cette macro : cliquer des paramètres dans l’onglet Chains | RackCard.MacroTab.cs |
| No targets yet. Press Map, then click parameters on the Chains tab. | Aucune cible pour l’instant. Appuyer sur Map, puis cliquer des paramètres dans l’onglet Chains. | RackCard.MacroTab.cs |
| Double-click to rename | Double-clic pour renommer | RackCard.MacroTab.cs |
| Map the chosen macro: click parameters in the chain | Assigner la macro choisie : cliquer des paramètres dans la chaîne | RackCard.Macros.cs |
| Not mapped · pick it and press Map | Non assigné · le choisir puis appuyer sur Map | RackCard.Macros.cs |
| {0} targets | {0} cible / {0} cibles | RackCard.Macros.cs |
| {0} → {1} · drag to turn · double-click resets | {0} → {1} · glisser pour tourner · double-clic pour réinitialiser | RackCard.Macros.cs |
| Stop mapping (Esc) | Arrêter l’assignation (Échap) | RackCard.Macros.cs |
| A rack keeps at least one chain | Un rack garde au moins une chaîne | RackCard.RightPanel.cs |
| All chains share one voice, the last key wins | Toutes les chaînes partagent une voix, la dernière touche l’emporte | RackCard.RightPanel.cs |
| Copy this chain after itself | Copier cette chaîne après elle-même | RackCard.RightPanel.cs |
| Edit on the {0} tab | Modifier dans l’onglet {0} | RackCard.RightPanel.cs |
| Glides between notes in mono mode | Glisse entre les notes en mode mono | RackCard.RightPanel.cs |
| In Select mode the selector follows the input level instead of its handle | En mode Select, le sélecteur suit le niveau d’entrée au lieu de sa poignée | RackCard.RightPanel.cs |
| Line up chains with different latency | Aligner des chaînes de latences différentes | RackCard.RightPanel.cs |
| No chain yet. | Aucune chaîne pour l’instant. | RackCard.RightPanel.cs |
| Notes no chain takes reach the instrument unchanged | Les notes qu’aucune chaîne ne prend atteignent l’instrument inchangées | RackCard.RightPanel.cs |
| Pass through outside zones | Laisser passer hors des zones | RackCard.RightPanel.cs |
| Two chains sending the same pitch play one note | Deux chaînes qui envoient la même hauteur ne jouent qu’une note | RackCard.RightPanel.cs |
| {0} · {1} · dry/wet {2} · {3} | {0} · {1} · dry/wet {2} · {3} | RackCard.RightPanel.cs |
| {0} · {1} → merged MIDI · {2} | {0} · {1} → MIDI fusionné · {2} | RackCard.RightPanel.cs |
| Delete this chain | Supprimer cette chaîne | RackCard.RightPanel.cs |
| Follow input level | Suivre le niveau d’entrée | RackCard.RightPanel.cs |
| Merge duplicate notes | Fusionner les notes en double | RackCard.RightPanel.cs |
| Mono (last note) | Mono (dernière note) | RackCard.RightPanel.cs |
| {0} macros mapped | {0} macro assignée / {0} macros assignées | RackCard.RightPanel.cs |
|  · follows input |  · suit l’entrée | RackCard.RightPanel.cs |
| Select {0} | Sélecteur {0} | RackCard.RightPanel.cs |
|  · inactive |  · inactif | RackCard.Zones.cs |
| Drag the edges to set the key zone · hover to test a note | Glisser les bords pour définir la zone de touches · survoler pour tester une note | RackCard.Zones.cs |
| Drag the edges to set the select range | Glisser les bords pour définir la plage de sélection | RackCard.Zones.cs |
| Drag the edges to set the velocity zone | Glisser les bords pour définir la zone de vélocité | RackCard.Zones.cs |
| Drag the selector or map it to a macro | Glisser le sélecteur ou l’assigner à une macro | RackCard.Zones.cs |
| Drag to choose which chains play · right-click to map it to a macro | Glisser pour choisir les chaînes jouées · clic droit pour l’assigner à une macro | RackCard.Zones.cs |
| Drag zone edges · hover to test a note | Glisser les bords de la zone · survoler pour tester une note | RackCard.Zones.cs |
| Map to a macro | Assigner à une macro | RackCard.Zones.cs |
| Ranges apply when Chains run is set to Select | Les plages s’appliquent quand Chains run est réglé sur Select | RackCard.Zones.cs |
| Select range: in Select mode this chain plays while the selector is inside it | Plage de sélection : en mode Select, cette chaîne joue tant que le sélecteur est dedans | RackCard.Zones.cs |
| The selector follows the input level · turn Follow input off on the Rack out tab to set it by hand | Le sélecteur suit le niveau d’entrée · désactiver Follow input dans l’onglet Rack out pour le régler à la main | RackCard.Zones.cs |
| nothing | rien | RackCard.Zones.cs |
| selector {0} → {1}{2} | sélecteur {0} → {1}{2} | RackCard.Zones.cs |
| silence | silence | RackCard.Zones.cs |
| {0} · vel {1} → {2} | {0} · vél. {1} → {2} | RackCard.Zones.cs |
| Keys C−1–G9 · vel 0–127 | Touches C−1–G9 · vél. 0–127 | RackCard.cs |
| {0} of 8 mapped | {0} sur 8 assignées | RackCard.cs |
| {0} chains | {0} chaîne / {0} chaînes | RackCard.cs |
| Chain {0} | Chaîne {0} | RackCard.cs |
|  · no choke groups |  · aucun groupe d’exclusion | RackCardView.Drum.cs |
|  · swing {0} · humanize {1} |  · swing {0} · humanize {1} | RackCardView.Drum.cs |
| A rack can't go on a Drum Rack pad. | Un rack ne peut pas aller sur un pad de Drum Rack. | RackCardView.Drum.cs |
| A synth voice, no sample | Une voix de synthé, sans échantillon | RackCardView.Drum.cs |
| Choke group: click for the next | Groupe d’exclusion : cliquer pour le suivant | RackCardView.Drum.cs |
| Drop a sample on a pad, or pick a kit above. | Déposer un échantillon sur un pad, ou choisir un kit ci-dessus. | RackCardView.Drum.cs |
| Hide the empty pads | Masquer les pads vides | RackCardView.Drum.cs |
| No pads in bank {0} — drop samples on the Pads view. | Aucun pad dans la banque {0} — déposer des échantillons dans la vue Pads. | RackCardView.Drum.cs |
| No pads in bank {0}. | Aucun pad dans la banque {0}. | RackCardView.Drum.cs |
| No pads to map | Aucun pad à assigner | RackCardView.Drum.cs |
| No sample — drop one on the pad | Aucun échantillon — en déposer un sur le pad | RackCardView.Drum.cs |
| Nudge every hit's timing at random | Décaler au hasard le timing de chaque frappe | RackCardView.Drum.cs |
| Open the pad's instrument | Ouvrir l’instrument du pad | RackCardView.Drum.cs |
| Pad bank: sixteen notes from C1, C2, C3 or C4 | Banque de pads : seize notes à partir de C1, C2, C3 ou C4 | RackCardView.Drum.cs |
| Pad pan · drag up or down, ⌥-click to type, double-click to centre | Panoramique du pad · glisser vers le haut ou le bas, ⌥-clic pour saisir, double-clic pour centrer | RackCardView.Drum.cs |
| Pad volume · drag up or down, ⌥-click to type, double-click for 0 dB | Volume du pad · glisser vers le haut ou le bas, ⌥-clic pour saisir, double-clic pour 0 dB | RackCardView.Drum.cs |
| Pads in the same group cut each other off | Les pads d’un même groupe se coupent mutuellement | RackCardView.Drum.cs |
| Push the off-beat sixteenths late | Retarder les doubles-croches en contretemps | RackCardView.Drum.cs |
| Select a pad to see its device chain. | Sélectionner un pad pour voir sa chaîne d’appareils. | RackCardView.Drum.cs |
| Shorten the pad's tail; hold lets it ring | Raccourcir la queue du pad ; hold la laisse sonner | RackCardView.Drum.cs |
| Show every pad of the bank | Afficher tous les pads de la banque | RackCardView.Drum.cs |
| Show the pad's instrument and effects | Afficher l’instrument et les effets du pad | RackCardView.Drum.cs |
| Transpose the note the pad plays, in semitones | Transposer la note jouée par le pad, en demi-tons | RackCardView.Drum.cs |
| choke group {0}: {1} | groupe d’exclusion {0} : {1} | RackCardView.Drum.cs |
| instrument, then its effects · signal left → right | instrument, puis ses effets · signal de gauche → droite | RackCardView.Drum.cs |
| {0} choke groups | {0} groupe d’exclusion / {0} groupes d’exclusion | RackCardView.Drum.cs |
| {0} effects | {0} effet / {0} effets | RackCardView.Drum.cs |
| {0} {1} selected · {2} · choke {3} | {0} {1} sélectionné · {2} · exclusion {3} | RackCardView.Drum.cs |
| {0} {1} · {2} → {3} → pad volume, pan | {0} {1} · {2} → {3} → volume, panoramique du pad | RackCardView.Drum.cs |
| {0} · no pad selected{1} | {0} · aucun pad sélectionné{1} | RackCardView.Drum.cs |
| {0} · {1} — click to play, right-click for more | {0} · {1} — cliquer pour jouer, clic droit pour plus d’options | RackCardView.Drum.cs |
| {0} · {1}/16 loaded | {0} · {1}/16 chargés | RackCardView.Drum.cs |
| {0} — click to add an instrument, or drop a sample here | {0} — cliquer pour ajouter un instrument, ou déposer un échantillon ici | RackCardView.Drum.cs |
| Back to pads | Retour aux pads | RackCardView.Drum.cs |
| Mute the pad | Couper le pad | RackCardView.Drum.cs |
| Select the pad | Sélectionner le pad | RackCardView.Drum.cs |
| Show the pads | Afficher les pads | RackCardView.Drum.cs |
| Solo the pad | Mettre le pad en solo | RackCardView.Drum.cs |
| Choke group | Groupe d’exclusion | RackCardView.Drum.cs |
| Open chain | Ouvrir la chaîne | RackCardView.Drum.cs |
| Remove pad | Retirer le pad | RackCardView.Drum.cs |
| None\|choke group | Aucun | RackCardView.Drum.cs |
| All {0} pads | Tous les {0} pad / Tous les {0} pads | RackCardView.Drum.cs |
| sampler {0} | sampler {0} | RackCardView.Drum.cs |
| {0} · empty | {0} · vide | RackCardView.Drum.cs |
| Add device — click for the list, or drop an effect here | Ajouter un appareil — cliquer pour la liste, ou déposer un effet ici | RackCardView.cs |
| Audio Effect Rack · chain {0} | Audio Effect Rack · chaîne {0} | RackCardView.cs |
| Drag to set · right-click to map | Glisser pour régler · clic droit pour assigner | RackCardView.cs |
| Drum Rack · pad {0}{1} | Drum Rack · pad {0}{1} | RackCardView.cs |
| Instrument Rack · chain {0}{1} | Instrument Rack · chaîne {0}{1} | RackCardView.cs |
| Map to the selector | Assigner au sélecteur | RackCardView.cs |
| Mapped to Macro {0} — click to change | Assigné à la macro {0} — cliquer pour changer | RackCardView.cs |
| Nota Rhythm · voice {0} | Nota Rhythm · voix {0} | RackCardView.cs |
| This Drum Rack is no longer in the rack. | Ce Drum Rack n’est plus dans le rack. | RackCardView.cs |
| {0} parameters | {0} paramètre / {0} paramètres | RackCardView.cs |
| {0} · effect {1} of {2} | {0} · effet {1} sur {2} | RackCardView.cs |
| {0} · voices {1} | {0} · voix {1} | RackCardView.cs |
| {0} · {1} pads | {0} · {1} pads | RackCardView.cs |
| Map to parameter | Assigner à un paramètre | RackCardView.cs |
| Unmap | Désassigner | RackCardView.cs |
| chain {0} | chaîne {0} | RackCardView.cs |
| {0} macros | {0} macro / {0} macros | RackCardView.cs |
| → {0} params | → {0} param. / → {0} param. | RackCardView.cs |
| All chains get the input. Outputs are summed. | Toutes les chaînes reçoivent l’entrée. Les sorties sont additionnées. | RackKindProfile.cs |
| Chain 1 feeds chain 2, top to bottom. | La chaîne 1 alimente la chaîne 2, de haut en bas. | RackKindProfile.cs |
| Drop MIDI effects here. Output goes to the track instrument. | Déposer des effets MIDI ici. La sortie va à l’instrument de la piste. | RackKindProfile.cs |
| Drop an effect to add a chain. | Déposer un effet pour ajouter une chaîne. | RackKindProfile.cs |
| Drop an instrument to add a layer. | Déposer un instrument pour ajouter une couche. | RackKindProfile.cs |
| Each note goes to every chain whose zone it hits. Outputs merge before the instrument. | Chaque note va à toutes les chaînes dont elle touche la zone. Les sorties fusionnent avant l’instrument. | RackKindProfile.cs |
| Only chains whose range holds the selector get notes. | Seules les chaînes dont la plage contient le sélecteur reçoivent des notes. | RackKindProfile.cs |
| Only chains whose range holds the selector play. | Seules les chaînes dont la plage contient le sélecteur jouent. | RackKindProfile.cs |
| range {0} — {1} · root {2} · keytrack {3} | plage {0} — {1} · fondamentale {2} · keytrack {3} | SamplerViews.cs |
| Frozen — its parts don't play until the track is unfrozen | Gelée — ses parties ne jouent pas tant que la piste n’est pas dégelée | SidechainSourcePicker.cs |
| No free routes — every sidechain route is in use | Aucune route libre — toutes les routes de sidechain sont utilisées | SidechainSourcePicker.cs |
| The keyed part was deleted — undo brings it back | La partie pilotée a été supprimée — annuler la rétablit | SidechainSourcePicker.cs |
| Part {0} | Partie {0} | SidechainSourcePicker.cs |
| {0} · missing | {0} · manquant | SidechainSourcePicker.cs |
| Select devices next to each other to group them | Sélectionner des appareils côte à côte pour les grouper | DeviceChainView.Clipboard.cs |
|  — its macros and chain settings were dropped |  — ses macros et réglages de chaîne ont été abandonnés | DeviceChainView.Clipboard.cs |
| A Drum Rack can't be ungrouped | Un Drum Rack ne peut pas être dégroupé | DeviceChainView.Clipboard.cs |
| A plug-in can't go in a MIDI Effect Rack | Un plug-in ne peut pas aller dans un MIDI Effect Rack | DeviceChainView.Clipboard.cs |
| A rack can't go inside another rack | Un rack ne peut pas aller dans un autre rack | DeviceChainView.Clipboard.cs |
| An Instrument Rack can't go inside another Instrument Rack | Un Instrument Rack ne peut pas aller dans un autre Instrument Rack | DeviceChainView.Clipboard.cs |
| Audio effect · {0} | Effet audio · {0} | DeviceChainView.Clipboard.cs |
| Couldn't paste the instrument. | Impossible de coller l’instrument. | DeviceChainView.Clipboard.cs |
| Couldn't paste: a device failed to load. | Impossible de coller : un appareil n’a pas pu se charger. | DeviceChainView.Clipboard.cs |
| MIDI effect · {0} | Effet MIDI · {0} | DeviceChainView.Clipboard.cs |
| Nothing to paste. | Rien à coller. | DeviceChainView.Clipboard.cs |
| Only a rack with one chain can be ungrouped | Seul un rack à une seule chaîne peut être dégroupé | DeviceChainView.Clipboard.cs |
| Only an instrument track takes MIDI effects and an instrument | Seule une piste d’instrument accepte des effets MIDI et un instrument | DeviceChainView.Clipboard.cs |
| Only an instrument track takes an instrument. | Seule une piste d’instrument accepte un instrument. | DeviceChainView.Clipboard.cs |
| Paste an instrument on an instrument card. | Coller un instrument sur une carte d’instrument. | DeviceChainView.Clipboard.cs |
| Pasted {0} devices, skipped {1} this track can't take | {0} appareils collés, {1} ignorés car cette piste ne peut pas les accepter | DeviceChainView.Clipboard.cs |
| Put {0} devices in {1} | Placer {0} appareils dans {1} | DeviceChainView.Clipboard.cs |
| Put {0} in {1} | Placer {0} dans {1} | DeviceChainView.Clipboard.cs |
| Select a rack to take its devices out | Sélectionner un rack pour en sortir les appareils | DeviceChainView.Clipboard.cs |
| Select devices to put them in a rack | Sélectionner des appareils pour les placer dans un rack | DeviceChainView.Clipboard.cs |
| Some devices can't be duplicated | Certains appareils ne peuvent pas être dupliqués | DeviceChainView.Clipboard.cs |
| The rack's chain has no instrument to put back | La chaîne du rack n’a aucun instrument à remettre | DeviceChainView.Clipboard.cs |
| These devices can't be copied, so they weren't cut. | Ces appareils ne peuvent pas être copiés, ils n’ont donc pas été coupés. | DeviceChainView.Clipboard.cs |
| These devices can't be grouped | Ces appareils ne peuvent pas être groupés | DeviceChainView.Clipboard.cs |
| This device can't be copied, so it wasn't cut. | Cet appareil ne peut pas être copié, il n’a donc pas été coupé. | DeviceChainView.Clipboard.cs |
| This rack can't be ungrouped | Ce rack ne peut pas être dégroupé | DeviceChainView.Clipboard.cs |
| This track can't take the copied devices. | Cette piste ne peut pas accepter les appareils copiés. | DeviceChainView.Clipboard.cs |
| Took the device out of the rack | Appareil sorti du rack | DeviceChainView.Clipboard.cs |
| Took {0} devices out of the rack | {0} appareils sortis du rack | DeviceChainView.Clipboard.cs |
| {0} devices | {0} appareil / {0} appareils | DeviceChainView.Clipboard.cs |
| Deleted {0} devices | {0} appareils supprimés | DeviceChainView.Clipboard.cs |
| Device | Appareil | DeviceChainView.Clipboard.cs |
| Group into rack | Grouper dans un rack | DeviceChainView.Clipboard.cs |
| Pasted {0} | {0} collé | DeviceChainView.Clipboard.cs |
| Pasted {0} devices | {0} appareils collés | DeviceChainView.Clipboard.cs |
| Selected | Sélection | DeviceChainView.Clipboard.cs |
| Ungroup rack | Dégrouper le rack | DeviceChainView.Clipboard.cs |
| With the instrument | Avec l’instrument | DeviceChainView.Clipboard.cs |
| {0} — double-click to unfold | {0} — double-clic pour déplier | DeviceChainView.Fold.cs |
| Remove device | Retirer l’appareil | DeviceChainView.Fold.cs |
| Remove track | Retirer la piste | DeviceChainView.Fold.cs |
| Unfold | Déplier | DeviceChainView.Fold.cs |
| Presets are unavailable. | Les préréglages ne sont pas disponibles. | DeviceChainView.Instruments.cs |
| Drums · {0} pads | Drums · {0} pad / Drums · {0} pads | DeviceChainView.Instruments.cs |
| Rack · {0} chains | Rack · {0} chaîne / Rack · {0} chaînes | DeviceChainView.Midi.cs |
| Move to track | Déplacer vers la piste | DeviceChainView.Move.cs |
| Replace… | Remplacer… | DeviceChainView.Move.cs |
| Mono | Mono | DeviceChainView.Shell.cs |
| Click to select · double-click to fold · right-click to replace, copy or move | Cliquer pour sélectionner · double-clic pour replier · clic droit pour remplacer, copier ou déplacer | DeviceChainView.Shell.cs |
| Copy A to B | Copier A vers B | DeviceChainView.Shell.cs |
| Copy B to A | Copier B vers A | DeviceChainView.Shell.cs |
| Double-click to fold · right-click for more | Double-clic pour replier · clic droit pour plus d’options | DeviceChainView.Shell.cs |
| Drag to reorder · double-click to fold · right-click for A/B, move and delete | Glisser pour réordonner · double-clic pour replier · clic droit pour A/B, déplacer et supprimer | DeviceChainView.Shell.cs |
| Off — click to turn the instrument on | Désactivé — cliquer pour activer l’instrument | DeviceChainView.Shell.cs |
| On — click to turn the instrument off | Activé — cliquer pour désactiver l’instrument | DeviceChainView.Shell.cs |
| Compare: {0} | Comparer : {0} | DeviceChainView.Shell.cs |
| Move left | Déplacer vers la gauche | DeviceChainView.Shell.cs |
| Move right | Déplacer vers la droite | DeviceChainView.Shell.cs |
| Preset: {0} | Préréglage : {0} | DeviceChainView.Shell.cs |
| Sends | Envois | DeviceChainView.cs |
| Send {0} | Envoi {0} | DeviceChainView.cs |
| Add an audio effect: search the browser, or drop a device here | Ajouter un effet audio : chercher dans le navigateur, ou déposer un appareil ici | DeviceChainView.cs |
| No return tracks | Aucune piste de retour | DeviceChainView.cs |
| Select a track | Sélectionner une piste | DeviceChainView.cs |
| Active — click to bypass | Actif — cliquer pour contourner | DeviceWindow.cs |
| Bypassed | Contourné | DeviceWindow.cs |
| Bypassed — click to enable | Contourné — cliquer pour activer | DeviceWindow.cs |
| Size: S (260 × 260) / L (700 × 260) | Taille : S (260 × 260) / L (700 × 260) | DeviceWindow.cs |
| Devices | Appareils | MainWindow.Detail.cs |
| Fill the window with the clip editor (⌘⇧F) | Remplir la fenêtre avec l’éditeur de clips (⌘⇧F) | MainWindow.Detail.cs |
| Select a track first. | Sélectionner d’abord une piste. | MainWindow.Detail.cs |
| Return {0} | Retour {0} | MainWindow.Detail.cs |
| Track {0} clip | Clip de la piste {0} | MainWindow.Detail.cs |
| Monitor in | Écoute entrée | MainWindow.Detail.cs |
| Audio  | Audio  | MainWindow.Detail.cs |
| Back to the split (⌘⇧F) | Retour à la vue partagée (⌘⇧F) | MainWindow.Detail.cs |
| Clip editor: every clip on the track | Éditeur de clips : tous les clips de la piste | MainWindow.Detail.cs |
| Clip editor: one clip | Éditeur de clips : un seul clip | MainWindow.Detail.cs |
| Group {0} | Groupe {0} | MainWindow.Detail.cs |
| In  | Entrée  | MainWindow.Detail.cs |
| Inst  | Inst  | MainWindow.Detail.cs |
| MIDI from  | MIDI depuis  | MainWindow.Detail.cs |
| PDC {0:0.0} ms | PDC {0:0.0} ms | MainWindow.Detail.cs |
| Select a MIDI clip on a Drum Rack track to step it. | Sélectionner un clip MIDI sur une piste Drum Rack pour le programmer pas à pas. | MainWindow.Detail.cs |
| Select a clip to edit it. | Sélectionner un clip pour le modifier. | MainWindow.Detail.cs |
| The clip editor is in its own window | L’éditeur de clips est dans sa propre fenêtre | MainWindow.Detail.cs |
| Track {0} · Scene {1} | Piste {0} · Scène {1} | MainWindow.Detail.cs |
| Select a clip in the arrangement to edit it here | Sélectionner un clip dans l’arrangement pour le modifier ici | MainWindow.DetailWindow.cs |
| Delete | Supprimer | MainWindow.DeviceMoves.cs |
| Cancel | Annuler | MainWindow.DeviceMoves.cs |
| Master | Master | MainWindow.DeviceMoves.cs |
|  · deleted {0} |  · {0} supprimé | MainWindow.DeviceMoves.cs |
|  · modulation links left on {0} |  · liaisons de modulation restantes sur {0} | MainWindow.DeviceMoves.cs |
| Couldn't copy {0}. | Impossible de copier {0}. | MainWindow.DeviceMoves.cs |
| Couldn't move {0}. | Impossible de déplacer {0}. | MainWindow.DeviceMoves.cs |
| Cut {0} — deleted {1} | {0} coupé — {1} supprimé | MainWindow.DeviceMoves.cs |
| Delete track | Supprimer la piste | MainWindow.DeviceMoves.cs |
| Delete track “{0}”? The track and its {1} selected devices will be removed. | Supprimer la piste « {0} » ? La piste et ses {1} appareils sélectionnés seront supprimés. | MainWindow.DeviceMoves.cs |
| Deleted {0} | {0} supprimé | MainWindow.DeviceMoves.cs |
| Move | Déplacer | MainWindow.DeviceMoves.cs |
| Move {0} devices to “{1}” and delete “{2}”? | Déplacer {0} appareils vers « {1} » et supprimer « {2} » ? | MainWindow.DeviceMoves.cs |
| Move {0} to “{1}” and delete “{2}” with its clips? | Déplacer {0} vers « {1} » et supprimer « {2} » avec ses clips ? | MainWindow.DeviceMoves.cs |
| Moved {0} devices to {1} | {0} appareils déplacés vers {1} | MainWindow.DeviceMoves.cs |
| Moved {0} devices to {1} · {2} couldn't move | {0} appareils déplacés vers {1} · {2} n’ont pas pu être déplacés | MainWindow.DeviceMoves.cs |
| “{0}” can't take {1}. | « {0} » ne peut pas accepter {1}. | MainWindow.DeviceMoves.cs |
| “{0}” has clips. Delete the track with its clips? | « {0} » contient des clips. Supprimer la piste avec ses clips ? | MainWindow.DeviceMoves.cs |
|  · sidechain cleared |  · sidechain supprimé | MainWindow.DeviceMoves.cs |
| Copied {0} to {1} | {0} copié vers {1} | MainWindow.DeviceMoves.cs |
| Moved {0} to {1} | {0} déplacé vers {1} | MainWindow.DeviceMoves.cs |
| Changed instrument to {0} (track {1}) | Instrument remplacé par {0} (piste {1}) | DeviceInsertService.cs |
| A Drum Rack takes kits and instruments; load an instrument preset on a pad from its card. | Un Drum Rack accepte des kits et des instruments ; charger un préréglage d’instrument sur un pad depuis sa carte. | DeviceInsertService.cs |
| Changed instrument to {0} | Instrument remplacé par {0} | DeviceInsertService.cs |
| MIDI effects need an instrument track. | Les effets MIDI nécessitent une piste d’instrument. | DeviceInsertService.cs |
| Added {0} to track {1} | {0} ajouté à la piste {1} | DeviceInsertService.cs |
| Replaced with {0} | Remplacé par {0} | DeviceInsertService.cs |
| Added {0} chain | Chaîne {0} ajoutée | DeviceInsertService.cs |
| All 16 pads are used. | Les 16 pads sont utilisés. | DeviceInsertService.cs |
| Added {0} pad | Pad {0} ajouté | DeviceInsertService.cs |
| Loaded {0} | {0} chargé | DeviceInsertService.cs |
| Loaded {0} · root {1} | {0} chargé · fondamentale {1} | DeviceInsertService.cs |
| Drop samples onto the arrangement. | Déposer des échantillons sur l’arrangement. | DeviceInsertService.cs |
| Drop MIDI files onto the arrangement or a session slot. | Déposer des fichiers MIDI sur l’arrangement ou un emplacement de session. | DeviceInsertService.cs |
| Drop onto a track. | Déposer sur une piste. | DeviceInsertService.cs |
| Failed to load effect. | Échec du chargement de l’effet. | DeviceInsertService.cs |
| {0} can't go on a track. | {0} ne peut pas aller sur une piste. | DeviceInsertService.cs |
| MIDI effects need an instrument track: add an instrument first. | Les effets MIDI nécessitent une piste d’instrument : ajouter d’abord un instrument. | DeviceInsertService.cs |
| Couldn't add a track for {0}. | Impossible d’ajouter une piste pour {0}. | DeviceInsertService.cs |
| Added {0} on a new audio track (track {1}) | {0} ajouté sur une nouvelle piste audio (piste {1}) | DeviceInsertService.cs |
| Applied preset {0} | Préréglage {0} appliqué | DeviceInsertService.cs |
| Kits are unavailable. | Les kits ne sont pas disponibles. | DeviceInsertService.cs |
| Loaded {0} kit | Kit {0} chargé | DeviceInsertService.cs |
| MIDI and audio effects go in one rack only with the instrument between them | Les effets MIDI et audio ne vont dans un même rack qu’avec l’instrument entre eux | DeviceSelection.cs |

## Catalogue d’appareils et catégories (114)

| English | Français | Source |
|---|---|---|
| 3-band EQ | EQ 3 bandes | BuiltinDeviceCatalog.cs |
| 8-band EQ | EQ 8 bandes | BuiltinDeviceCatalog.cs |
| A single gain stage with a big readout. | Un seul étage de gain avec un grand affichage. | BuiltinDeviceCatalog.cs |
| A synth with a built-in pattern engine for arpeggios and sequences. | Un synthé avec un moteur de motifs intégré pour arpèges et séquences. | BuiltinDeviceCatalog.cs |
| Algorithmic reverb from small rooms to large halls. | Réverbération algorithmique, des petites pièces aux grandes salles. | BuiltinDeviceCatalog.cs |
| Auto-pan and tremolo synced to the tempo. | Auto-pan et trémolo synchronisés au tempo. | BuiltinDeviceCatalog.cs |
| Bit-depth and sample-rate reduction. | Réduction de la profondeur de bits et de la fréquence d’échantillonnage. | BuiltinDeviceCatalog.cs |
| Brickwall limiter for the master. | Limiteur brickwall pour le master. | BuiltinDeviceCatalog.cs |
| Captures and repeats slices for stutters and glitches. | Capture et répète des tranches pour des bégaiements et des glitchs. | BuiltinDeviceCatalog.cs |
| Chorus and ensemble for width and shimmer. | Chorus et ensemble pour la largeur et le scintillement. | BuiltinDeviceCatalog.cs |
| Classic jet flanger. | Flanger jet classique. | BuiltinDeviceCatalog.cs |
| Compressor with sidechain and five characters — glue, punch and pumping. | Compresseur avec sidechain et cinq caractères — glue, punch et pumping. | BuiltinDeviceCatalog.cs |
| EQ bands that react to the level — de-essing and resonance control. | Bandes d’EQ qui réagissent au niveau — dé-essage et contrôle des résonances. | BuiltinDeviceCatalog.cs |
| Eight-band parametric EQ with a live spectrum. | EQ paramétrique à huit bandes avec spectre en direct. | BuiltinDeviceCatalog.cs |
| Eight-voice drum machine with a step sequencer. | Boîte à rythmes à huit voix avec séquenceur pas à pas. | BuiltinDeviceCatalog.cs |
| FM synth | synthé FM | BuiltinDeviceCatalog.cs |
| FM synthesis — bells, electric pianos and hard basses. | Synthèse FM — cloches, pianos électriques et basses dures. | BuiltinDeviceCatalog.cs |
| Five-voice vintage polysynth — lush pads, brass and keys. | Polysynthé vintage à cinq voix — nappes riches, cuivres et claviers. | BuiltinDeviceCatalog.cs |
| Forces note lengths — staccato, legato or fixed. | Impose les durées de notes — staccato, legato ou fixes. | BuiltinDeviceCatalog.cs |
| Gain, width, mono and phase — the housekeeping tool. | Gain, largeur, mono et phase — l’outil de base. | BuiltinDeviceCatalog.cs |
| Granular synth that turns a sample into clouds, textures and drones. | Synthé granulaire qui transforme un échantillon en nuages, textures et drones. | BuiltinDeviceCatalog.cs |
| Guitar amp and cabinet models from clean to high gain. | Modèles d’amplis et de baffles de guitare, du son clair au fort gain. | BuiltinDeviceCatalog.cs |
| Hybrid reverb with early reflections and a long, dense tail. | Réverbération hybride avec premières réflexions et longue queue dense. | BuiltinDeviceCatalog.cs |
| Keeps notes in a key and scale. | Garde les notes dans une tonalité et une gamme. | BuiltinDeviceCatalog.cs |
| Layered looper with overdubs. | Looper à couches avec superpositions. | BuiltinDeviceCatalog.cs |
| Layers several instruments in parallel chains. | Superpose plusieurs instruments dans des chaînes parallèles. | BuiltinDeviceCatalog.cs |
| Modelled electric piano — tine, suitcase, reed and clav with tremolo, phaser and cabinet. | Piano électrique modélisé — tine, suitcase, reed et clav avec trémolo, phaser et baffle. | BuiltinDeviceCatalog.cs |
| Monophonic analog-style synth for fat basses and leads. | Synthé monophonique de style analogique pour basses épaisses et leads. | BuiltinDeviceCatalog.cs |
| Multi-stage phaser. | Phaser multi-étages. | BuiltinDeviceCatalog.cs |
| Multisample instrument — zones by key and velocity, round-robin and release samples; SFZ import and packs. | Instrument multi-échantillons — zones par touche et vélocité, round-robin et échantillons de relâchement ; import SFZ et packs. | BuiltinDeviceCatalog.cs |
| Noise gate and rhythmic trance gate. | Noise gate et trance gate rythmique. | BuiltinDeviceCatalog.cs |
| Paraphonic ensemble synth — string machines and choirs. | Synthé d’ensemble paraphonique — string machines et chœurs. | BuiltinDeviceCatalog.cs |
| Physical modelling of struck, plucked and blown bodies. | Modélisation physique de corps frappés, pincés et soufflés. | BuiltinDeviceCatalog.cs |
| Plays a sample across the keyboard. | Joue un échantillon sur tout le clavier. | BuiltinDeviceCatalog.cs |
| Random transposition, timing and dropouts. | Transposition, timing et silences aléatoires. | BuiltinDeviceCatalog.cs |
| Real-time pitch correction to a key and scale. | Correction de hauteur en temps réel vers une tonalité et une gamme. | BuiltinDeviceCatalog.cs |
| Reshapes, compresses or randomises note velocities. | Remodèle, comprime ou randomise les vélocités des notes. | BuiltinDeviceCatalog.cs |
| Resonant filter swept by an envelope follower or an LFO. | Filtre résonant balayé par un suiveur d’enveloppe ou un LFO. | BuiltinDeviceCatalog.cs |
| Runs several effect chains in parallel. | Exécute plusieurs chaînes d’effets en parallèle. | BuiltinDeviceCatalog.cs |
| Shapes this track's sound with another track's voice. | Façonne le son de cette piste avec la voix d’une autre piste. | BuiltinDeviceCatalog.cs |
| Sixteen pads, each with its own sample or instrument chain. | Seize pads, chacun avec son propre échantillon ou sa chaîne d’instruments. | BuiltinDeviceCatalog.cs |
| Spectrum analyser, oscilloscope and level meter. | Analyseur de spectre, oscilloscope et vumètre. | BuiltinDeviceCatalog.cs |
| Splits notes across chains of MIDI effects by key, velocity or a selector. | Répartit les notes entre des chaînes d’effets MIDI selon la touche, la vélocité ou un sélecteur. | BuiltinDeviceCatalog.cs |
| Step arpeggiator with groove lanes. | Arpégiateur pas à pas avec voies de groove. | BuiltinDeviceCatalog.cs |
| Stereo and ping-pong delay synced to the tempo. | Delay stéréo et ping-pong synchronisé au tempo. | BuiltinDeviceCatalog.cs |
| Tape and tube saturation — warmth, wow and flutter. | Saturation à bande et à lampes — chaleur, wow et flutter. | BuiltinDeviceCatalog.cs |
| Three-band DJ-style EQ with kills. | EQ trois bandes façon DJ avec kills. | BuiltinDeviceCatalog.cs |
| Three-band upward and downward compression — OTT-style density. | Compression ascendante et descendante en trois bandes — densité façon OTT. | BuiltinDeviceCatalog.cs |
| Turns single notes into chords. | Transforme des notes seules en accords. | BuiltinDeviceCatalog.cs |
| Two-oscillator analog-style synth with drive and a unison stack — pads, basses, acid lines and leads. | Synthé de style analogique à deux oscillateurs avec drive et pile d’unissons — nappes, basses, lignes acid et leads. | BuiltinDeviceCatalog.cs |
| Vector synth morphing between sources — moving pads and textures. | Synthé vectoriel qui fond entre des sources — nappes et textures mouvantes. | BuiltinDeviceCatalog.cs |
| Waveshaping saturator from soft warmth to hard clipping. | Saturateur à mise en forme d’onde, de la chaleur douce à l’écrêtage dur. | BuiltinDeviceCatalog.cs |
| Wavetable synth with warping oscillators — evolving pads and modern leads. | Synthé à tables d’ondes avec oscillateurs déformants — nappes évolutives et leads modernes. | BuiltinDeviceCatalog.cs |
| amplifier | amplificateur | BuiltinDeviceCatalog.cs |
| analog synth | synthé analogique | BuiltinDeviceCatalog.cs |
| analyzer / scope | analyseur / oscilloscope | BuiltinDeviceCatalog.cs |
| arp synth | synthé arpégiateur | BuiltinDeviceCatalog.cs |
| auto-pan | auto-pan | BuiltinDeviceCatalog.cs |
| bit crusher | bit crusher | BuiltinDeviceCatalog.cs |
| built-in | intégrés | BuiltinDeviceCatalog.cs |
| chains of MIDI effects | chaînes d’effets MIDI | BuiltinDeviceCatalog.cs |
| chorus | chorus | BuiltinDeviceCatalog.cs |
| drum machine | boîte à rythmes | BuiltinDeviceCatalog.cs |
| dynamic EQ | EQ dynamique | BuiltinDeviceCatalog.cs |
| electric piano | piano électrique | BuiltinDeviceCatalog.cs |
| envelope / LFO | enveloppe / LFO | BuiltinDeviceCatalog.cs |
| flanger | flanger | BuiltinDeviceCatalog.cs |
| force note length | durée de note imposée | BuiltinDeviceCatalog.cs |
| gain | gain | BuiltinDeviceCatalog.cs |
| gate | gate | BuiltinDeviceCatalog.cs |
| glitch & repeats | glitch et répétitions | BuiltinDeviceCatalog.cs |
| granular synth | synthé granulaire | BuiltinDeviceCatalog.cs |
| hybrid reverb | réverbération hybride | BuiltinDeviceCatalog.cs |
| limiter | limiteur | BuiltinDeviceCatalog.cs |
| looper | looper | BuiltinDeviceCatalog.cs |
| mono synth | synthé mono | BuiltinDeviceCatalog.cs |
| multiband dynamics | dynamique multibande | BuiltinDeviceCatalog.cs |
| multisample | multi-échantillons | BuiltinDeviceCatalog.cs |
| paraphonic synth | synthé paraphonique | BuiltinDeviceCatalog.cs |
| phaser | phaser | BuiltinDeviceCatalog.cs |
| physical synth | synthé à modélisation physique | BuiltinDeviceCatalog.cs |
| pitch correction | correction de hauteur | BuiltinDeviceCatalog.cs |
| pitch quantize | quantification de hauteur | BuiltinDeviceCatalog.cs |
| poly synth | synthé polyphonique | BuiltinDeviceCatalog.cs |
| random transpose | transposition aléatoire | BuiltinDeviceCatalog.cs |
| saturator | saturateur | BuiltinDeviceCatalog.cs |
| stacked intervals | intervalles superposés | BuiltinDeviceCatalog.cs |
| step arpeggiator | arpégiateur pas à pas | BuiltinDeviceCatalog.cs |
| vector-morph synth | synthé à morphing vectoriel | BuiltinDeviceCatalog.cs |
| velocity shaping | façonnage de la vélocité | BuiltinDeviceCatalog.cs |
| vintage saturator | saturateur vintage | BuiltinDeviceCatalog.cs |
| vocoder | vocoder | BuiltinDeviceCatalog.cs |
| wavetable synth | synthé à tables d’ondes | BuiltinDeviceCatalog.cs |
| Distortion | Distorsion | DeviceCategories.cs |
| Dynamics | Dynamique | DeviceCategories.cs |
| EQ & filter | EQ et filtre | DeviceCategories.cs |
| Keys & physical | Claviers et modélisation | DeviceCategories.cs |
| Modulation | Modulation | DeviceCategories.cs |
| Pitch & time | Hauteur et temps | DeviceCategories.cs |
| Reverb & delay | Réverbération et delay | DeviceCategories.cs |
| Samplers | Samplers | DeviceCategories.cs |
| Synths | Synthés | DeviceCategories.cs |
| Utility | Utilitaires | DeviceCategories.cs |
| A low-frequency oscillator, free or synced to the tempo. | Un oscillateur basse fréquence, libre ou synchronisé au tempo. | ModularCatalog.cs |
| Adds, multiplies or mixes two control signals. | Additionne, multiplie ou mélange deux signaux de contrôle. | ModularCatalog.cs |
| An attack-decay-sustain-release envelope triggered by notes. | Une enveloppe attaque-déclin-maintien-relâchement déclenchée par les notes. | ModularCatalog.cs |
| CV utility | Utilitaire CV | ModularCatalog.cs |
| Control | Contrôle | ModularCatalog.cs |
| Converter | Convertisseur | ModularCatalog.cs |
| Modulator | Modulateur | ModularCatalog.cs |
| One knob driving several destinations. | Un potentiomètre qui pilote plusieurs destinations. | ModularCatalog.cs |
| Shows a control signal over time. | Affiche un signal de contrôle au fil du temps. | ModularCatalog.cs |
| Turns notes, velocity and controllers into control signals. | Transforme notes, vélocité et contrôleurs en signaux de contrôle. | ModularCatalog.cs |
| Turns the track's loudness into a control signal: a peak controller, for ducking one track under another. | Transforme le volume de la piste en signal de contrôle : un contrôleur de crête, pour faire ducker une piste sous une autre. | ModularCatalog.cs |

## Dialogues, fichiers et messages d’état (344)

| English | Français | Source |
|---|---|---|
| Cancelling… | Annulation… | DownloadJobs.cs |
| Installing {0} failed: {1} | Échec de l’installation de {0} : {1} | DownloadJobs.cs |
| Installing {0} was cancelled. | L’installation de {0} a été annulée. | DownloadJobs.cs |
| Sample rate | Fréquence d’échantillonnage | ExportWindow.cs |
| Export audio | Exporter l’audio | ExportWindow.cs |
| Add 1-bar release tail | Ajouter une queue de relâchement d’1 mesure | ExportWindow.cs |
| Bar.beat.sixteenth, counted from 1 — e.g. 5.1 or 5.3.2 | Mesure.temps.double-croche, comptés à partir de 1 — p. ex. 5.1 ou 5.3.2 | ExportWindow.cs |
| Bit depth | Profondeur de bits | ExportWindow.cs |
| Custom | Personnalisé | ExportWindow.cs |
| Dither | Dithering | ExportWindow.cs |
| End | Fin | ExportWindow.cs |
| Export WAV | Exporter en WAV | ExportWindow.cs |
| Export stems | Exporter les stems | ExportWindow.cs |
| Export {0} | Exporter {0} | ExportWindow.cs |
| Format | Format | ExportWindow.cs |
| Full song | Morceau entier | ExportWindow.cs |
| Normalize −1 dBTP | Normaliser à −1 dBTP | ExportWindow.cs |
| Not a position: type bar.beat, like 5.1 | Position invalide : saisir mesure.temps, comme 5.1 | ExportWindow.cs |
| Quality | Qualité | ExportWindow.cs |
| The end must be after the start | La fin doit être après le début | ExportWindow.cs |
| one file per track · {0:0.0} MB each · offline render | un fichier par piste · {0:0.0} Mo chacun · rendu hors ligne | ExportWindow.cs |
| · one file per track, post-fader | · un fichier par piste, post-fader | ExportWindow.cs |
| 16-bit only | 16 bits uniquement | ExportWindow.cs |
| ≈ 1 file · {0:0.0} MB · offline render | ≈ 1 fichier · {0:0.0} Mo · rendu hors ligne | ExportWindow.cs |
| Positions are bar.beat.sixteenth, counted from 1 as in the transport: 5 or 5.1 is the start of bar 5, 5.3 its third beat. The export stops at the end position, so 1 to 5 is four bars. | Les positions s’écrivent mesure.temps.double-croche, comptées à partir de 1 comme dans le transport : 5 ou 5.1 est le début de la mesure 5, 5.3 son troisième temps. L’export s’arrête à la position de fin, donc 1 à 5 fait quatre mesures. | ExportWindow.cs |
| Using Nota's built-in file dialog: {0}. Check xdg-desktop-portal and its FileChooser backend. | Utilisation de la boîte de dialogue de fichiers intégrée de Nota : {0}. Vérifier xdg-desktop-portal et son moteur FileChooser. | LinuxFileDialogs.cs |
| the system file dialog answered without showing | la boîte de dialogue système a répondu sans s’afficher | LinuxFileDialogs.cs |
| the system file dialog didn't open within {0:0} s | la boîte de dialogue système ne s’est pas ouverte en {0:0} s | LinuxFileDialogs.cs |
| the system file dialog failed: {0} | échec de la boîte de dialogue système : {0} | LinuxFileDialogs.cs |
| Device default | Par défaut du périphérique | MainWindow.AudioFormat.cs |
| Buffer size | Taille de la mémoire tampon | MainWindow.AudioFormat.cs |
| Driver settings… | Réglages du pilote… | MainWindow.AudioFormat.cs |
| Audio settings… | Réglages audio… | MainWindow.AudioFormat.cs |
| Exclusive mode uses the device's buffer | Le mode exclusif utilise le tampon de l’appareil | MainWindow.AudioFormat.cs |
| Set in the ASIO driver's settings | À régler dans les réglages du pilote ASIO | MainWindow.AudioFormat.cs |
| Stop recording to change it | Arrêter l’enregistrement pour le modifier | MainWindow.AudioFormat.cs |
| Failed to load {0} | Échec du chargement de {0} | MainWindow.AudioImport.cs |
| Failed to load {0}: {1} | Échec du chargement de {0} : {1} | MainWindow.AudioImport.cs |
| Import of {0} cancelled | Import de {0} annulé | MainWindow.AudioImport.cs |
| Imported {0} | {0} importé | MainWindow.AudioImport.cs |
| Importing {0}… | Import de {0}… | MainWindow.AudioImport.cs |
| Warping {0}… {1:0} % | Warp de {0}… {1:0} % | MainWindow.AudioImport.cs |
| loop warped {0} → {1} BPM | boucle warpée {0} → {1} BPM | MainWindow.AudioImport.cs |
| one-shot, not warped | one-shot, non warpé | MainWindow.AudioImport.cs |
| transposed {0} st to {1} | transposé de {0} st vers {1} | MainWindow.AudioImport.cs |
| warped to tempo ({0:0.0} BPM) | warpé au tempo ({0:0.0} BPM) | MainWindow.AudioImport.cs |
| {0} · Processing… | {0} · Traitement… | MainWindow.AudioImport.cs |
| {0} · Processing… {1:0} % | {0} · Traitement… {1:0} % | MainWindow.AudioImport.cs |
| Nothing to undo | Rien à annuler | MainWindow.Commands.cs |
| Select a clip or a range first | Sélectionner d’abord un clip ou une plage | MainWindow.Commands.cs |
| Select clips or a range first | Sélectionner d’abord des clips ou une plage | MainWindow.Commands.cs |
| Select a time range first | Sélectionner d’abord une plage de temps | MainWindow.Commands.cs |
| Toggled clip(s) | État des clips inversé | MainWindow.Commands.cs |
| Select a clip first | Sélectionner d’abord un clip | MainWindow.Commands.cs |
| Open a clip in the clip editor first | Ouvrir d’abord un clip dans l’éditeur de clips | MainWindow.Commands.cs |
| Select a track first | Sélectionner d’abord une piste | MainWindow.Commands.cs |
| Grouped tracks | Pistes groupées | MainWindow.Commands.cs |
| Select tracks first | Sélectionner d’abord des pistes | MainWindow.Commands.cs |
| Ungrouped | Dégroupé | MainWindow.Commands.cs |
| This track can't be frozen | Cette piste ne peut pas être gelée | MainWindow.Commands.cs |
| Open the Session view first | Ouvrir d’abord la vue Session | MainWindow.Commands.cs |
| Scenes live in the Session view | Les scènes se trouvent dans la vue Session | MainWindow.Commands.cs |
| Open a MIDI clip first | Ouvrir d’abord un clip MIDI | MainWindow.Commands.cs |
| Quantized notes | Notes quantifiées | MainWindow.Commands.cs |
| Select two or more MIDI clips | Sélectionner au moins deux clips MIDI | MainWindow.Commands.cs |
| No notes outside the scale | Aucune note hors de la gamme | MainWindow.Commands.cs |
| Envelopes locked — clip moves keep automation in place | Enveloppes verrouillées : les déplacements de clips laissent l’automation en place | MainWindow.Commands.cs |
| Envelopes follow clips | Les enveloppes suivent les clips | MainWindow.Commands.cs |
| Selecting a track leaves the arms alone | Sélectionner une piste ne modifie pas l’armement | MainWindow.Commands.cs |
| Selecting an instrument track arms it | Sélectionner une piste d’instrument l’arme | MainWindow.Commands.cs |
| Track headers on the left | En-têtes de piste à gauche | MainWindow.Commands.cs |
| Track headers on the right | En-têtes de piste à droite | MainWindow.Commands.cs |
| Snap off | Magnétisme désactivé | MainWindow.Commands.cs |
| Arrangement grid: {0} | Grille de l’arrangement : {0} | MainWindow.Commands.cs |
| Clip editor grid: {0} | Grille de l’éditeur de clips : {0} | MainWindow.Commands.cs |
| Analysing… | Analyse… | MainWindow.Convert.cs |
| Building pads… | Création des pads… | MainWindow.Convert.cs |
| Convert failed: {0} | Échec de la conversion : {0} | MainWindow.Convert.cs |
| Converted harmony: {0} notes → new track {1} | Harmonie convertie : {0} notes → nouvelle piste {1} | MainWindow.Convert.cs |
| Converted melody: {0} notes → new track {1} | Mélodie convertie : {0} notes → nouvelle piste {1} | MainWindow.Convert.cs |
| Converted {0} hits | {0} frappes converties | MainWindow.Convert.cs |
| Converted {0}: {1} notes → new track {2} | {0} converti : {1} notes → nouvelle piste {2} | MainWindow.Convert.cs |
| Detecting harmony… | Détection de l’harmonie… | MainWindow.Convert.cs |
| Detecting hits… | Détection des frappes… | MainWindow.Convert.cs |
| Detecting pitch… | Détection de la hauteur… | MainWindow.Convert.cs |
| Detecting transients… | Détection des transitoires… | MainWindow.Convert.cs |
| Listening for notes… | À l’écoute des notes… | MainWindow.Convert.cs |
| No drum hits found. | Aucune frappe de batterie trouvée. | MainWindow.Convert.cs |
| No harmony found. | Aucune harmonie trouvée. | MainWindow.Convert.cs |
| No melody found. | Aucune mélodie trouvée. | MainWindow.Convert.cs |
| No {0} found. | Aucun {0} trouvé. | MainWindow.Convert.cs |
| Nothing to slice. | Rien à découper. | MainWindow.Convert.cs |
| Sliced into {0} pads | Découpé en {0} pads | MainWindow.Convert.cs |
| Slicing pads… {0} | Découpe des pads… {0} | MainWindow.Convert.cs |
| That converter isn't available yet. | Ce convertisseur n’est pas encore disponible. | MainWindow.Convert.cs |
| This clip has no audio to convert. | Ce clip n’a pas d’audio à convertir. | MainWindow.Convert.cs |
| {0} → new Drum Rack track {1} | {0} → nouvelle piste Drum Rack {1} | MainWindow.Convert.cs |
| Drop failed: {0} | Échec du dépôt : {0} | MainWindow.Dnd.cs |
| Couldn't add {0} to the rack. | Impossible d’ajouter {0} au rack. | MainWindow.Dnd.cs |
| Drop effects onto a column. | Déposer des effets sur une colonne. | MainWindow.Dnd.cs |
| Drop samples and MIDI files onto a slot. | Déposer des échantillons et des fichiers MIDI sur un emplacement. | MainWindow.Dnd.cs |
| Drop samples onto audio slots. | Déposer des échantillons sur les emplacements audio. | MainWindow.Dnd.cs |
| Dismiss | Ignorer | MainWindow.Downloads.cs |
| Open Settings → Downloads | Ouvrir Réglages → Téléchargements | MainWindow.Downloads.cs |
| New project | Nouveau projet | MainWindow.Files.cs |
| Export | Exporter | MainWindow.Files.cs |
| Export failed: {0} | Échec de l’export : {0} | MainWindow.Files.cs |
| Export master to {0} | Exporter le master en {0} | MainWindow.Files.cs |
| Export stems to folder | Exporter les stems dans un dossier | MainWindow.Files.cs |
| Exported {0} | {0} exporté | MainWindow.Files.cs |
| Exported {0} stems | {0} stem exporté / {0} stems exportés | MainWindow.Files.cs |
| Exporting stems… | Export des stems… | MainWindow.Files.cs |
| Exporting {0}… {1:0} % | Export de {0}… {1:0} % | MainWindow.Files.cs |
| Exporting… | Export… | MainWindow.Files.cs |
| Import audio | Importer de l’audio | MainWindow.Files.cs |
| New project. | Nouveau projet. | MainWindow.Files.cs |
| Nota project | Projet Nota | MainWindow.Files.cs |
| Nota — {0} | Nota — {0} | MainWindow.Files.cs |
| Nothing to export — the arrangement is empty. | Rien à exporter — l’arrangement est vide. | MainWindow.Files.cs |
| Open Nota project (.nota folder) | Ouvrir un projet Nota (dossier .nota) | MainWindow.Files.cs |
| Open failed: {0} | Échec de l’ouverture : {0} | MainWindow.Files.cs |
| Open project | Ouvrir un projet | MainWindow.Files.cs |
| Opened {0} | {0} ouvert | MainWindow.Files.cs |
| Opened {0} — {1} item(s) downgraded: {2} | {0} ouvert — {1} élément(s) dégradé(s) : {2} | MainWindow.Files.cs |
| Preparing warped clips… | Préparation des clips warpés… | MainWindow.Files.cs |
| Preparing warped clips… {0:0} % | Préparation des clips warpés… {0:0} % | MainWindow.Files.cs |
| Save Nota project | Enregistrer le projet Nota | MainWindow.Files.cs |
| Save failed: {0} | Échec de l’enregistrement : {0} | MainWindow.Files.cs |
| Save your changes before opening another project? | Enregistrer les modifications avant d’ouvrir un autre projet ? | MainWindow.Files.cs |
| Save your changes before starting a new project? | Enregistrer les modifications avant de commencer un nouveau projet ? | MainWindow.Files.cs |
| Saved {0} | {0} enregistré | MainWindow.Files.cs |
| Saved {0} — {1} unsupported item(s) skipped: {2} | {0} enregistré — {1} élément(s) non pris en charge ignoré(s) : {2} | MainWindow.Files.cs |
| Unsupported file location. | Emplacement de fichier non pris en charge. | MainWindow.Files.cs |
| Unsupported project location. | Emplacement de projet non pris en charge. | MainWindow.Files.cs |
| Untitled | Sans titre | MainWindow.Files.cs |
| {0} audio | Audio {0} | MainWindow.Files.cs |
| Freeze track | Geler la piste | MainWindow.Freeze.cs |
| Couldn't freeze this track. | Impossible de geler cette piste. | MainWindow.Freeze.cs |
| Discard edits | Abandonner les modifications | MainWindow.Freeze.cs |
| Done — re-freeze | Terminé — geler à nouveau | MainWindow.Freeze.cs |
| Edit source | Modifier la source | MainWindow.Freeze.cs |
| Flatten | Aplatir | MainWindow.Freeze.cs |
| Flatten (remove source) | Aplatir (supprimer la source) | MainWindow.Freeze.cs |
| Flatten failed: {0} | Échec de l’aplatissement : {0} | MainWindow.Freeze.cs |
| Flatten replaces this track's instrument, MIDI and devices with a single audio clip of the frozen sound.  | Aplatir remplace l’instrument, le MIDI et les appareils de cette piste par un seul clip audio du son gelé.  | MainWindow.Freeze.cs |
| Flatten to audio track | Aplatir en piste audio | MainWindow.Freeze.cs |
| Flatten track | Aplatir la piste | MainWindow.Freeze.cs |
| Freeze failed: {0} | Échec du gel : {0} | MainWindow.Freeze.cs |
| Freeze the track first, then Flatten. | Geler d’abord la piste, puis l’aplatir. | MainWindow.Freeze.cs |
| Freeze {0} tracks | Geler {0} pistes | MainWindow.Freeze.cs |
| Freezing track {0} of {1}… {2:0} % | Gel de la piste {0} sur {1}… {2:0} % | MainWindow.Freeze.cs |
| Freezing tracks… | Gel des pistes… | MainWindow.Freeze.cs |
| Freezing track… | Gel de la piste… | MainWindow.Freeze.cs |
| Freezing track… {0:0} % | Gel de la piste… {0:0} % | MainWindow.Freeze.cs |
| Froze {0} of {1} tracks. | {0} pistes sur {1} gelées. | MainWindow.Freeze.cs |
| Frozen | Gelée | MainWindow.Freeze.cs |
| Nothing to freeze — the arrangement is empty. | Rien à geler — l’arrangement est vide. | MainWindow.Freeze.cs |
| This can't be undone. Continue? | Cette action est irréversible. Continuer ? | MainWindow.Freeze.cs |
| Track flattened to audio. | Piste aplatie en audio. | MainWindow.Freeze.cs |
| Track frozen — its devices are idle until you unfreeze. | Piste gelée — ses appareils sont inactifs jusqu’au dégel. | MainWindow.Freeze.cs |
| Track unfrozen. | Piste dégelée. | MainWindow.Freeze.cs |
| Unfreeze track | Dégeler la piste | MainWindow.Freeze.cs |
| Unfreeze {0} tracks | Dégeler {0} pistes | MainWindow.Freeze.cs |
| {0} tracks frozen — their devices are idle until you unfreeze. | {0} pistes gelées — leurs appareils sont inactifs jusqu’au dégel. | MainWindow.Freeze.cs |
| {0} tracks unfrozen. | {0} pistes dégelées. | MainWindow.Freeze.cs |
| Gamepad octave: C{0} | Octave manette : C{0} | MainWindow.Gamepad.cs |
| Octave + | Octave + | MainWindow.Gamepad.cs |
| Octave − | Octave − | MainWindow.Gamepad.cs |
| Velocity + | Vélocité + | MainWindow.Gamepad.cs |
| Velocity − | Vélocité − | MainWindow.Gamepad.cs |
|  · version not recorded: {0} |  · version non enregistrée : {0} | MainWindow.History.cs |
| Version history turned off · {0} freed | Historique des versions désactivé · {0} libérés | MainWindow.History.cs |
| Save version | Enregistrer la version | MainWindow.History.cs |
| Switch version | Changer de version | MainWindow.History.cs |
| Save your changes as a new version before switching? | Enregistrer les modifications comme nouvelle version avant de changer ? | MainWindow.History.cs |
| Couldn't switch version: {0} | Impossible de changer de version : {0} | MainWindow.History.cs |
| Switched to {0} | Passage à {0} | MainWindow.History.cs |
| Open version as copy | Ouvrir la version comme copie | MainWindow.History.cs |
| Save your changes before opening the copy? | Enregistrer les modifications avant d’ouvrir la copie ? | MainWindow.History.cs |
| Open version as a new project | Ouvrir la version comme nouveau projet | MainWindow.History.cs |
| Choose a new name for the copy — it can't replace an existing project. | Choisir un nouveau nom pour la copie — elle ne peut pas remplacer un projet existant. | MainWindow.History.cs |
| Couldn't open the version: {0} | Impossible d’ouvrir la version : {0} | MainWindow.History.cs |
| Opened {0} as {1} | {0} ouvert comme {1} | MainWindow.History.cs |
| the version of {0} | la version du {0} | MainWindow.History.cs |
| the version “{0}” ({1}) | la version “{0}” ({1}) | MainWindow.History.cs |
| Copied automation | Automation copiée | MainWindow.Input.cs |
| Copied clip(s) | Clip(s) copié(s) | MainWindow.Input.cs |
| Cut clip(s) | Clip(s) coupé(s) | MainWindow.Input.cs |
| Deleted automation | Automation supprimée | MainWindow.Input.cs |
| Deleted range | Plage supprimée | MainWindow.Input.cs |
| Keyboard octave: C{0} | Octave clavier : C{0} | MainWindow.Input.cs |
| Keyboard velocity: {0} | Vélocité clavier : {0} | MainWindow.Input.cs |
| Pasted automation | Automation collée | MainWindow.Input.cs |
| Pasted clip(s) | Clip(s) collé(s) | MainWindow.Input.cs |
| Editing the frozen source — press Done to re-freeze, or Discard. | Modification de la source gelée — appuyer sur Terminé pour geler à nouveau, ou sur Abandonner. | MainWindow.LiveFreeze.cs |
| Flatten keeps the frozen audio track and permanently removes its source (instrument, MIDI and devices). Continue? | Aplatir conserve la piste audio gelée et supprime définitivement sa source (instrument, MIDI et appareils). Continuer ? | MainWindow.LiveFreeze.cs |
| Flattened — the frozen audio is now a plain track. | Aplati — l’audio gelé est maintenant une piste ordinaire. | MainWindow.LiveFreeze.cs |
| Left edit mode (the frozen audio was not changed). | Mode d’édition quitté (l’audio gelé n’a pas été modifié). | MainWindow.LiveFreeze.cs |
| Live freeze failed: {0} | Échec du gel en direct : {0} | MainWindow.LiveFreeze.cs |
| Live-freeze link removed — its track was deleted. | Lien de gel en direct supprimé — sa piste a été supprimée. | MainWindow.LiveFreeze.cs |
| Re-freeze failed — the source is still live. | Échec du nouveau gel — la source est toujours active. | MainWindow.LiveFreeze.cs |
| Re-freeze failed: {0} | Échec du nouveau gel : {0} | MainWindow.LiveFreeze.cs |
| Rendering… | Rendu… | MainWindow.LiveFreeze.cs |
| Rendering… {0:0} % | Rendu… {0:0} % | MainWindow.LiveFreeze.cs |
| Track live-frozen — press Edit to change it, then Done. | Piste gelée en direct — appuyer sur Modifier pour la changer, puis sur Terminé. | MainWindow.LiveFreeze.cs |
| Unfrozen — the source is live again. | Dégelée — la source est de nouveau active. | MainWindow.LiveFreeze.cs |
| {0} failed: {1} | Échec de {0} : {1} | MainWindow.LiveFreeze.cs |
| Added {0} to slot | {0} ajouté à l’emplacement | MainWindow.MidiImport.cs |
| Couldn't read {0}: {1} | Impossible de lire {0} : {1} | MainWindow.MidiImport.cs |
| Drop MIDI files onto instrument slots. | Déposer des fichiers MIDI sur les emplacements d’instruments. | MainWindow.MidiImport.cs |
| Imported {0} ({1} parts) | {0} importé ({1} parties) | MainWindow.MidiImport.cs |
| {0} has no notes. | {0} ne contient aucune note. | MainWindow.MidiImport.cs |
| Missing plugins | Plug-ins manquants | MainWindow.MissingPlugins.cs |
| This project uses {0}, which isn't installed. It's open source and available from the Nota plugin registry. Install and reopen the project? | Ce projet utilise {0}, qui n’est pas installé. Il est open source et disponible dans le registre de plug-ins de Nota. L’installer et rouvrir le projet ? | MainWindow.MissingPlugins.cs |
| This project uses {0}, which aren't installed. They're open source and available from the Nota plugin registry. Install and reopen the project? | Ce projet utilise {0}, qui ne sont pas installés. Ils sont open source et disponibles dans le registre de plug-ins de Nota. Les installer et rouvrir le projet ? | MainWindow.MissingPlugins.cs |
| Not now | Pas maintenant | MainWindow.MissingPlugins.cs |
| Installing plugins… | Installation des plug-ins… | MainWindow.MissingPlugins.cs |
| Scanning plugins… | Analyse des plug-ins… | MainWindow.MissingPlugins.cs |
| Installed {0} — reopen the project to use it. | {0} installé — rouvrir le projet pour l’utiliser. | MainWindow.MissingPlugins.cs |
| Installed {0} — reopen the project to use them. | {0} installés — rouvrir le projet pour les utiliser. | MainWindow.MissingPlugins.cs |
| Created the preset {0} | Préréglage {0} créé | MainWindow.Mosaic.cs |
| Created {0} presets · loaded {1} | {0} préréglages créés · {1} chargé | MainWindow.Mosaic.cs |
| Click an audio track to paste the bounced audio onto. | Cliquer une piste audio pour y coller l’audio rendu. | MainWindow.PasteBounced.cs |
| Couldn't paste the bounced audio. | Impossible de coller l’audio rendu. | MainWindow.PasteBounced.cs |
| Couldn't render this track. | Impossible de rendre cette piste. | MainWindow.PasteBounced.cs |
| Paste bounced audio failed: {0} | Échec du collage de l’audio rendu : {0} | MainWindow.PasteBounced.cs |
| Paste bounced audio takes a range on a single track. | Coller l’audio rendu demande une plage sur une seule piste. | MainWindow.PasteBounced.cs |
| Pasted bounced audio | Audio rendu collé | MainWindow.PasteBounced.cs |
| Select a time range first (Shift+drag), then paste it bounced onto an audio track. | Sélectionner d’abord une plage de temps (Maj+glisser), puis la coller rendue sur une piste audio. | MainWindow.PasteBounced.cs |
| The selected range has no audio or instrument track to bounce. | La plage sélectionnée ne contient aucune piste audio ou d’instrument à rendre. | MainWindow.PasteBounced.cs |
| {0} is no longer installed. | {0} n’est plus installé. | MainWindow.Recent.cs |
| Couldn't load {0}. | Impossible de charger {0}. | MainWindow.Recent.cs |
| Added {0} (track {1}) | {0} ajouté (piste {1}) | MainWindow.Recent.cs |
| Unsaved changes | Modifications non enregistrées | MainWindow.Recovery.cs |
| Do you really want to quit without saving your changes? | Quitter sans enregistrer les modifications ? | MainWindow.Recovery.cs |
| unsaved changes · {0} | modifications non enregistrées · {0} | MainWindow.Recovery.cs |
| Nota didn't shut down cleanly. Recover unsaved work from {0}? | Nota ne s’est pas fermé correctement. Récupérer le travail non enregistré de {0} ? | MainWindow.Recovery.cs |
| Recovered unsaved work. | Travail non enregistré récupéré. | MainWindow.Recovery.cs |
| Recover project | Récupérer le projet | MainWindow.Recovery.cs |
| Discard | Ignorer | MainWindow.Recovery.cs |
| Remote is on · scan the code with a phone | Télécommande activée · scanner le code avec un téléphone | MainWindow.Remote.cs |
| Undo (from a phone) | Annuler (depuis un téléphone) | MainWindow.Remote.cs |
| {0} phones connected | {0} téléphone connecté / {0} téléphones connectés | MainWindow.Remote.cs |
| Download cancelled. | Téléchargement annulé. | MainWindow.Stems.cs |
| Downloading… | Téléchargement… | MainWindow.Stems.cs |
| Preparing audio… | Préparation de l’audio… | MainWindow.Stems.cs |
| Separate Stems | Séparer les stems | MainWindow.Stems.cs |
| Separate Stems cancelled. | Séparation des stems annulée. | MainWindow.Stems.cs |
| Separate Stems failed: {0} | Échec de la séparation des stems : {0} | MainWindow.Stems.cs |
| Separated "{0}" into drums, bass, other and vocals. | « {0} » séparé en batterie, basse, autres et voix. | MainWindow.Stems.cs |
| You can remove it later in Settings → Downloads → AI Models. | Il pourra être supprimé plus tard dans Réglages → Téléchargements → Modèles d’IA. | MainWindow.Stems.cs |
| the {0} model | le modèle {0} | MainWindow.Stems.cs |
| the {0} model and the AI runtime it runs on | le modèle {0} et le moteur d’IA sur lequel il tourne | MainWindow.Stems.cs |
| {0} is waiting for its model — it's downloading (see the status bar). | {0} attend son modèle — il se télécharge (voir la barre d’état). | MainWindow.Stems.cs |
| {0} isn't available on this computer. | {0} n’est pas disponible sur cet ordinateur. | MainWindow.Stems.cs |
| {0} uses an AI model that runs on this computer. Download {1} ({2}) once?  | {0} utilise un modèle d’IA qui s’exécute sur cet ordinateur. Télécharger {1} ({2}) une fois ?  | MainWindow.Stems.cs |
| Later | Plus tard | MainWindow.Updates.cs |
| Restart now | Redémarrer maintenant | MainWindow.Updates.cs |
| Update | Mettre à jour | MainWindow.Updates.cs |
| Try again | Réessayer | MainWindow.Updates.cs |
| The update didn't download | La mise à jour n’a pas pu être téléchargée | MainWindow.Updates.cs |
| Nota {0} is out — you have {1}. | Nota {0} est disponible — la version installée est la {1}. | MainWindow.Updates.cs |
| Release notes | Notes de version | MainWindow.Updates.cs |
|  Download it from the release page. |  Le télécharger depuis la page de la version. | MainWindow.Updates.cs |
|  Nota downloads it here and installs it when you restart or quit. |  Nota le télécharge ici et l’installe au redémarrage ou à la fermeture. | MainWindow.Updates.cs |
| Cancel the download | Annuler le téléchargement | MainWindow.Updates.cs |
| Check for updates | Rechercher des mises à jour | MainWindow.Updates.cs |
| Checking for updates… | Recherche de mises à jour… | MainWindow.Updates.cs |
| Couldn't reach GitHub to check for a newer version of Nota. Check your internet connection and try again. | Impossible de joindre GitHub pour rechercher une version plus récente de Nota. Vérifier la connexion internet puis réessayer. | MainWindow.Updates.cs |
| Download Nota {0}… | Télécharger Nota {0}… | MainWindow.Updates.cs |
| Hide — it still installs when you quit | Masquer — l’installation se fera quand même à la fermeture | MainWindow.Updates.cs |
| Nota {0} is available | Nota {0} est disponible | MainWindow.Updates.cs |
| Nota {0} is downloaded. Click to restart now — or it installs the next time you quit. | Nota {0} est téléchargé. Cliquer pour redémarrer maintenant — ou il s’installera à la prochaine fermeture. | MainWindow.Updates.cs |
| Nota {0} is ready to install. Restart Nota to finish updating — or it installs the next time you quit. | Nota {0} est prêt à être installé. Redémarrer Nota pour terminer la mise à jour — ou il s’installera à la prochaine fermeture. | MainWindow.Updates.cs |
| Restart to update to Nota {0} | Redémarrer pour passer à Nota {0} | MainWindow.Updates.cs |
| Update available | Mise à jour disponible | MainWindow.Updates.cs |
| Update to Nota {0} | Mettre à jour vers Nota {0} | MainWindow.Updates.cs |
| You have {0}. Click to update or read the release notes. | La version installée est {0}. Cliquer pour mettre à jour ou lire les notes de version. | MainWindow.Updates.cs |
| You're up to date: Nota {0} is the latest version. | Nota est à jour : la version {0} est la plus récente. | MainWindow.Updates.cs |
| Multisamples | Multi-échantillons | MosaicCreateWindow.cs |
| Create multisample | Créer un multi-échantillon | MosaicCreateWindow.cs |
| by name stem | par radical du nom | MosaicCreateWindow.cs |
| Octave | Octave | MosaicCreateWindow.cs |
| Mapping | Assignation | MosaicCreateWindow.cs |
| next to gap | à côté du trou | MosaicCreateWindow.cs |
| Reading the files… | Lecture des fichiers… | MosaicCreateWindow.cs |
| Create presets | Créer des préréglages | MosaicCreateWindow.cs |
| Presets will appear in Nota Mosaic → Packs → {0} | Les préréglages apparaîtront dans Nota Mosaic → Packs → {0} | MosaicCreateWindow.cs |
| {0} files · {1} | {0} fichiers · {1} | MosaicCreateWindow.cs |
| {0} layer | {0} couche | MosaicCreateWindow.cs |
| No audio files with a note in their name or a clear pitch. | Aucun fichier audio avec une note dans son nom ou une hauteur claire. | MosaicCreateWindow.cs |
| The names are taken as written (C4 = 60). | Les noms sont pris tels quels (C4 = 60). | MosaicCreateWindow.cs |
| No repeat | Sans répétition | MosaicCreateWindow.cs |
| Seq | Séq | MosaicCreateWindow.cs |
| Edge zone stretch | Étirement des zones de bord | MosaicCreateWindow.cs |
| Velocity layers by dynamics | Couches de vélocité par dynamique | MosaicCreateWindow.cs |
| Release samples → Release group | Échantillons de relâchement → groupe de relâchement | MosaicCreateWindow.cs |
| nothing to map | rien à assigner | MosaicCreateWindow.cs |
| {0}–{1} · {2} zones | {0}–{1} · {2} zones | MosaicCreateWindow.cs |
| Gap at {0} — neighbours stretched | Trou à {0} — voisines étirées | MosaicCreateWindow.cs |
| {0} without a note — left out | {0} sans note — ignoré | MosaicCreateWindow.cs |
| {0} · analysis | {0} · analyse | MosaicCreateWindow.cs |
| {0} · MIDI | {0} · MIDI | MosaicCreateWindow.cs |
| Create 1 preset | Créer 1 préréglage | MosaicCreateWindow.cs |
| Create {0} presets | Créer {0} préréglages | MosaicCreateWindow.cs |
| Mapping… | Assignation… | MosaicCreateWindow.cs |
| Round-robin | Round-robin | MosaicCreateWindow.cs |
| {0} duplicates | {0} doublon / {0} doublons | MosaicCreateWindow.cs |
| {0} notes from sample analysis | {0} note issue de l’analyse de l’échantillon / {0} notes issues de l’analyse de l’échantillon | MosaicCreateWindow.cs |
| Looking for plugins… | Recherche de plug-ins… | PluginScan.cs |
| Scanning plugins · {0:N0} of {1:N0} | Analyse des plug-ins · {0:N0} sur {1:N0} | PluginScan.cs |
| Ext | Ext | RecordInputPick.cs |
| Audio input 1 (mono) | Entrée audio 1 (mono) | RecordInputPick.cs |
| Audio input 2 (mono) | Entrée audio 2 (mono) | RecordInputPick.cs |
| Ext 1 | Ext 1 | RecordInputPick.cs |
| Ext 2 | Ext 2 | RecordInputPick.cs |
| Audio inputs 1/2 (stereo) | Entrées audio 1/2 (stéréo) | RecordInputPick.cs |
| Address | Adresse | ConnectPhoneView.cs |
| Allow | Autoriser | ConnectPhoneView.cs |
| Code | Code | ConnectPhoneView.cs |
| Connect phone | Connecter un téléphone | ConnectPhoneView.cs |
| Disconnect this phone (it stays trusted — forget it in Settings → Remote) | Déconnecter ce téléphone (il reste de confiance — l’oublier dans Réglages → Télécommande) | ConnectPhoneView.cs |
| Point the phone camera at the code. Over the cable the link is instant and needs no Wi-Fi; the code changes every 2 minutes and after each connection. | Pointer la caméra du téléphone vers le code. Par câble, la liaison est instantanée et sans Wi-Fi ; le code change toutes les 2 minutes et après chaque connexion. | ConnectPhoneView.cs |
| Point the phone camera at the code. Same Wi-Fi only — or plug in a USB cable (USB tethering on the phone; Internet Sharing on the Mac) for an instant link. The code changes every 2 minutes and after each connection. | Pointer la caméra du téléphone vers le code. Même Wi-Fi uniquement — ou brancher un câble USB (partage de connexion USB sur le téléphone ; Partage Internet sur le Mac) pour une liaison instantanée. Le code change toutes les 2 minutes et après chaque connexion. | ConnectPhoneView.cs |
| Remote is off. Turn it on to show a QR code a phone on the same Wi-Fi can scan. | La télécommande est désactivée. L’activer pour afficher un code QR qu’un téléphone sur le même Wi-Fi peut scanner. | ConnectPhoneView.cs |
| Remote is off. Turn it on to show a QR code. The first time, Windows asks whether Nota may use the network; allow private networks. | La télécommande est désactivée. L’activer pour afficher un code QR. La première fois, Windows demande si Nota peut utiliser le réseau ; autoriser les réseaux privés. | ConnectPhoneView.cs |
| Remote is off. Turn it on to show a QR code. The first time, macOS asks to allow Nota on the local network; click Allow. | La télécommande est désactivée. L’activer pour afficher un code QR. La première fois, macOS demande d’autoriser Nota sur le réseau local ; cliquer sur Autoriser. | ConnectPhoneView.cs |
| This computer isn't on a network. Join the same Wi-Fi as the phone, share the phone's hotspot with it, or plug in a USB cable (USB tethering on the phone; Internet Sharing on the Mac). | Cet ordinateur n’est pas sur un réseau. Rejoindre le même Wi-Fi que le téléphone, partager le point d’accès du téléphone avec lui, ou brancher un câble USB (partage de connexion USB sur le téléphone ; Partage Internet sur le Mac). | ConnectPhoneView.cs |
| USB | USB | ConnectPhoneView.cs |
| connected | connecté | ConnectPhoneView.cs |
| connecting | connexion | ConnectPhoneView.cs |
| {0} ms · weak network | {0} ms · réseau faible | ConnectPhoneView.cs |
| USB · {0} ms · {1} | USB · {0} ms · {1} | ConnectPhoneView.cs |
| {0} ms · {1} | {0} ms · {1} | ConnectPhoneView.cs |
| Port {0} is in use by another app. Pick another port. | Le port {0} est utilisé par une autre application. Choisir un autre port. | RemoteService.cs |
| Don't save | Ne pas enregistrer | SaveChangesWindow.cs |
| Saving… | Enregistrement… | SaveChangesWindow.cs |
| Close | Fermer | TagEditorWindow.cs |
| Add tag | Ajouter une étiquette | TagEditorWindow.cs |
| Edit tags | Modifier les étiquettes | TagEditorWindow.cs |
| New tag | Nouvelle étiquette | TagEditorWindow.cs |
| No tags yet — add one below. | Aucune étiquette pour l’instant — en ajouter une ci-dessous. | TagEditorWindow.cs |
| Tags — title & colour | Étiquettes — titre et couleur | TagEditorWindow.cs |
| OK | OK | TextPromptWindow.cs |

## Messages des couches basses (téléchargements, mises à jour, aperçus, erreurs) (70)

| English | Français | Source |
|---|---|---|
| Loading the model… | Chargement du modèle… | ClipAi.cs |
| Separating stems… | Séparation des stems… | ClipAi.cs |
| Writing stems… | Écriture des stems… | ClipAi.cs |
| Nota couldn't load the AI runtime. Remove it in Settings → Downloads → AI Models and install it again. | Nota n’a pas pu charger le moteur d’IA. Le supprimer dans Réglages → Téléchargements → Modèles d’IA puis le réinstaller. | OnnxRuntimeLoader.cs |
| The AI model file is damaged. Remove it in Settings → Downloads → AI Models and install it again. | Le fichier du modèle d’IA est endommagé. Le supprimer dans Réglages → Téléchargements → Modèles d’IA puis le réinstaller. | OnnxRuntimeLoader.cs |
| Kit not found. | Kit introuvable. | PresetAudition.Kits.cs |
| Plug-in presets can't be previewed — load the plug-in to hear them. | Les préréglages de plug-in ne peuvent pas être pré-écoutés — charger le plug-in pour les entendre. | PresetAudition.cs |
| Preset not found. | Préréglage introuvable. | PresetAudition.cs |
| Racks play what their chains hold — nothing to preview on its own. | Les racks jouent ce que contiennent leurs chaînes — rien à pré-écouter seul. | PresetAudition.cs |
| The Effect Rack plays what its chains hold — nothing to preview on its own. | L’Effect Rack joue ce que contiennent ses chaînes — rien à pré-écouter seul. | PresetAudition.cs |
| Couldn't create a Drum Rack track. | Impossible de créer une piste Drum Rack. | DrumKitService.cs |
| Couldn't create a Nota Rhythm track. | Impossible de créer une piste Nota Rhythm. | DrumKitService.cs |
| Unknown drum kit. | Kit de batterie inconnu. | DrumKitService.cs |
| AI runtime | Moteur d’IA | ModelCatalog.cs |
| Hears every note in a recording, chords included, for Convert Melody and Convert Harmony. | Entend chaque note d’un enregistrement, accords compris, pour Convertir la mélodie et Convertir l’harmonie. | ModelCatalog.cs |
| Splits a mix into drums, bass, vocals and everything else, each on its own track. | Sépare un mix en batterie, basse, voix et tout le reste, chacun sur sa propre piste. | ModelCatalog.cs |
| The library Nota's AI models run on. Installed with the first model. | La bibliothèque sur laquelle tournent les modèles d’IA de Nota. Installée avec le premier modèle. | ModelCatalog.cs |
| Unpacking {0}… | Décompression de {0}… | ModelStore.cs |
| AI models aren't available for this computer: ONNX Runtime has no build for it. | Les modèles d’IA ne sont pas disponibles pour cet ordinateur : ONNX Runtime n’en propose pas de version. | ModelStore.cs |
| Couldn't remove {0} — it may be in use. Restart Nota and try again. | Impossible de supprimer {0} — il est peut-être utilisé. Redémarrer Nota puis réessayer. | ModelStore.cs |
| Installing this model needs {0} of free disk space, and there is {1}. | L’installation de ce modèle demande {0} d’espace disque libre, et il y en a {1}. | ModelStore.cs |
| Invalid model id "{0}". | Identifiant de modèle « {0} » invalide. | ModelStore.cs |
| The download of {0} doesn't contain {1}. | Le téléchargement de {0} ne contient pas {1}. | ModelStore.cs |
| Unknown AI model "{0}". | Modèle d’IA « {0} » inconnu. | ModelStore.cs |
| Couldn't load the preset: {0} | Impossible de charger le préréglage : {0} | MosaicLibrary.cs |
| Couldn't load the sample "{0}". | Impossible de charger l’échantillon « {0} ». | PresetService.cs |
| Couldn't prepare the multisample "{0}". | Impossible de préparer le multi-échantillon « {0} ». | PresetService.cs |
| Couldn't prepare the sample "{0}". | Impossible de préparer l’échantillon « {0} ». | PresetService.cs |
| Failed to add MIDI effect. | Échec de l’ajout de l’effet MIDI. | PresetService.cs |
| Failed to add device. | Échec de l’ajout de l’appareil. | PresetService.cs |
| Failed to add instrument. | Échec de l’ajout de l’instrument. | PresetService.cs |
| Failed to load instrument. | Échec du chargement de l’instrument. | PresetService.cs |
| Failed to load plugin. | Échec du chargement du plug-in. | PresetService.cs |
| Not a MIDI effect. | Ce n’est pas un effet MIDI. | PresetService.cs |
| Not an effect. | Ce n’est pas un effet. | PresetService.cs |
| Plugin not installed: {0} | Plug-in non installé : {0} | PresetService.cs |
| Preset can't be applied in place. | Le préréglage ne peut pas être appliqué sur place. | PresetService.cs |
| Unknown preset type. | Type de préréglage inconnu. | PresetService.cs |
| This version was saved by Nota {0}, which is newer than this one. Update Nota to open it. | Cette version a été enregistrée par Nota {0}, plus récent que celui-ci. Mettre Nota à jour pour l’ouvrir. | ProjectHistory.cs |
| A .{0} archive can only be unpacked on macOS. | Une archive .{0} ne peut être décompressée que sous macOS. | ArchiveUnpacker.cs |
| The .deb archive has no data. | L’archive .deb ne contient aucune donnée. | ArchiveUnpacker.cs |
| The .deb archive is damaged. | L’archive .deb est endommagée. | ArchiveUnpacker.cs |
| The .deb archive is truncated. | L’archive .deb est tronquée. | ArchiveUnpacker.cs |
| The archive contains an unsafe path: {0} | L’archive contient un chemin dangereux : {0} | ArchiveUnpacker.cs |
| Unpacking needs "{0}", which wasn't found. | La décompression demande « {0} », introuvable. | ArchiveUnpacker.cs |
| Unsupported archive type "{0}". | Type d’archive « {0} » non pris en charge. | ArchiveUnpacker.cs |
| {0} failed ({1}): {2} | Échec de {0} ({1}) : {2} | ArchiveUnpacker.cs |
| Installing {0}… | Installation de {0}… | PluginStore.cs |
| Couldn't download {0}: {1} | Impossible de télécharger {0} : {1} | RegistryClient.cs |
| Couldn't reach the {0}. Check your internet connection. | Impossible de joindre le {0}. Vérifier la connexion internet. | RegistryClient.cs |
| The download of {0} doesn't match the registry's checksum — not installing it. | Le téléchargement de {0} ne correspond pas à la somme de contrôle du registre — il n’est pas installé. | RegistryClient.cs |
| The download of {0} is larger than the registry says — not installing it. | Le téléchargement de {0} est plus volumineux que ce qu’indique le registre — il n’est pas installé. | RegistryClient.cs |
| Unsafe path in the registry entry: {0} | Chemin dangereux dans l’entrée du registre : {0} | RegistryClient.cs |
| The sample registry needs a newer version of Nota. | Le registre d’échantillons demande une version plus récente de Nota. | SampleIndex.cs |
| Couldn't remove the pack's files — close anything using them and try again. | Impossible de supprimer les fichiers du pack — fermer ce qui les utilise puis réessayer. | SampleStore.cs |
| Invalid pack id "{0}". | Identifiant de pack « {0} » invalide. | SampleStore.cs |
| The download of {0} contains no samples Nota can use. | Le téléchargement de {0} ne contient aucun échantillon utilisable par Nota. | SampleStore.cs |
| The sample registry index is damaged. | L’index du registre d’échantillons est endommagé. | SampleStore.cs |
| Unpacking {0} needs {1} of free space on the system drive, which has {2}. | La décompression de {0} demande {1} d’espace libre sur le disque système, qui en a {2}. | SampleStore.cs |
| {0} needs {1} of free space in your Samples folder, which has {2}. | {0} demande {1} d’espace libre dans le dossier Échantillons, qui en a {2}. | SampleStore.cs |
| Preparing the update… | Préparation de la mise à jour… | AppUpdater.cs |
| Couldn't prepare the update ({0}: {1}). | Impossible de préparer la mise à jour ({0} : {1}). | AppUpdater.cs |
| Couldn't start {0}. | Impossible de lancer {0}. | AppUpdater.cs |
| Nota {0} has no download for this computer. | Nota {0} n’a pas de téléchargement pour cet ordinateur. | AppUpdater.cs |
| The downloaded Nota.app isn't the expected version — not installing it. | Le Nota.app téléchargé n’est pas la version attendue — il n’est pas installé. | AppUpdater.cs |
| The update disk image doesn't contain Nota.app. | L’image disque de la mise à jour ne contient pas Nota.app. | AppUpdater.cs |
| The update download is damaged (checksum mismatch) — not installing it. | Le téléchargement de la mise à jour est endommagé (somme de contrôle différente) — il n’est pas installé. | AppUpdater.cs |
| The update download is larger than expected — not installing it. | Le téléchargement de la mise à jour est plus volumineux que prévu — il n’est pas installé. | AppUpdater.cs |
| This copy of Nota can't update itself — download the new version from the release page. | Cet exemplaire de Nota ne peut pas se mettre à jour lui-même — télécharger la nouvelle version depuis la page de publication. | AppUpdater.cs |
| {0} timed out. | {0} : délai dépassé. | AppUpdater.cs |

## Application (libellés partagés) (2)

| English | Français | Source |
|---|---|---|
| of all CPUs | de tous les CPU | IProcessCpu.cs |
| of one core | d’un cœur | IProcessCpu.cs |

## Autres (89)

| English | Français | Source |
|---|---|---|
| Attack {0} | Attaque {0} | ClipAdsrKit.cs |
| Decay {0} · Sustain {1} | Déclin {0} · Maintien {1} | ClipAdsrKit.cs |
| Release {0} | Relâchement {0} | ClipAdsrKit.cs |
| Nota {0} | Nota {0} | HistoryView.cs |
| Versions | Versions | HistoryView.cs |
| Clean up the history | Nettoyer l’historique | HistoryView.cs |
| Save the project to start its history. | Enregistrer le projet pour commencer son historique. | HistoryView.cs |
| Version history is turned off in Settings. | L’historique des versions est désactivé dans les Réglages. | HistoryView.cs |
| Each save adds a version here. | Chaque enregistrement ajoute une version ici. | HistoryView.cs |
| No starred versions yet. Right-click a version and choose Star. | Aucune version étoilée pour l’instant. Clic droit sur une version puis « Étoiler ». | HistoryView.cs |
| Show all versions | Afficher toutes les versions | HistoryView.cs |
| Show starred versions only | Afficher seulement les versions étoilées | HistoryView.cs |
| Saved by a newer Nota — update to open it. | Enregistré par une version plus récente de Nota — mettre à jour pour l’ouvrir. | HistoryView.cs |
| Current | Actuelle | HistoryView.cs |
| Switch | Basculer | HistoryView.cs |
| Switch the project to this version | Basculer le projet vers cette version | HistoryView.cs |
| The project is at this version | Le projet est à cette version | HistoryView.cs |
| Open copy | Ouvrir une copie | HistoryView.cs |
| Open this version as a new project, keeping this one as it is | Ouvrir cette version comme nouveau projet, en laissant celui-ci tel quel | HistoryView.cs |
| Current version | Version actuelle | HistoryView.cs |
| Version | Version | HistoryView.cs |
| Switch to this version | Basculer vers cette version | HistoryView.cs |
| Open as copy… | Ouvrir comme copie… | HistoryView.cs |
| Name version | Nommer la version | HistoryView.cs |
| Name… | Nommer… | HistoryView.cs |
| Version name | Nom de la version | HistoryView.cs |
| Remove name | Retirer le nom | HistoryView.cs |
| Add note… | Ajouter une note… | HistoryView.cs |
| Edit note… | Modifier la note… | HistoryView.cs |
| Version note | Note de version | HistoryView.cs |
| What changed in this version? | Qu’est-ce qui a changé dans cette version ? | HistoryView.cs |
| Remove note | Retirer la note | HistoryView.cs |
| Remove star | Retirer l’étoile | HistoryView.cs |
| Star | Étoiler | HistoryView.cs |
| Delete version… | Supprimer la version… | HistoryView.cs |
| Switch to another version to delete this one | Basculer vers une autre version pour supprimer celle-ci | HistoryView.cs |
| Delete unnamed versions… | Supprimer les versions sans nom… | HistoryView.cs |
| Delete unnamed versions | Supprimer les versions sans nom | HistoryView.cs |
| Delete version | Supprimer la version | HistoryView.cs |
| Delete {0}? Audio that only this version uses is deleted from the project folder too. This can't be undone. | Supprimer {0} ? L’audio utilisé uniquement par cette version est aussi supprimé du dossier du projet. Cette action est irréversible. | HistoryView.cs |
| Couldn't change the version: {0} | Impossible de changer de version : {0} | HistoryView.cs |
| Its versions can't be listed until the file .history/versions.json is repaired. | Ses versions ne peuvent pas être listées tant que le fichier .history/versions.json n’est pas réparé. | HistoryView.cs |
| Delete {0} versions and the audio only they use? The current version, the latest version of each branch, and every named, starred or noted version stay. This can't be undone. | Supprimer {0} version et l’audio qu’elle seule utilise ? La version actuelle, la dernière version de chaque branche et toute version nommée, étoilée ou annotée restent. Cette action est irréversible. / Supprimer {0} versions et l’audio que seules elles utilisent ? La version actuelle, la dernière version de chaque branche et toute version nommée, étoilée ou annotée restent. Cette action est irréversible. | HistoryView.cs |
| Deleted {0} versions | {0} version supprimée / {0} versions supprimées | HistoryView.cs |
| {0} versions | {0} version / {0} versions | HistoryView.cs |
| {0} · older versions take {1} on disk | {0} · les anciennes versions occupent {1} sur le disque | HistoryView.cs |
| {0} · {1} freed | {0} · {1} libérés | HistoryView.cs |
| Delete {0} unnamed versions… | Supprimer {0} version sans nom… / Supprimer {0} versions sans nom… | HistoryView.cs |
| Left stick ←→ | Stick gauche ←→ | MidiLearn.cs |
| Left stick ↑↓ | Stick gauche ↑↓ | MidiLearn.cs |
| Right stick ←→ | Stick droit ←→ | MidiLearn.cs |
| Right stick ↑↓ | Stick droit ↑↓ | MidiLearn.cs |
| Left trigger | Gâchette gauche | MidiLearn.cs |
| Right trigger | Gâchette droite | MidiLearn.cs |
| Button {0} | Bouton {0} | MidiLearn.cs |
| Pad · {0} | Pad · {0} | MidiLearn.cs |
| Phone · {0} | Téléphone · {0} | MidiLearn.cs |
| Note {0} · ch{1} | Note {0} · canal {1} | MidiLearn.cs |
| Sample | Échantillon | PreviewPlayer.cs |
|  (auto for this effect) |  (auto pour cet effet) | PreviewPlayer.cs |
| Auto ({0}) | Auto ({0}) | PreviewPlayer.cs |
| Auto-audition: play a preset or device on select | Pré-écoute auto : jouer un préréglage ou un appareil à la sélection | PreviewPlayer.cs |
| Auto-audition: play a sample on select | Pré-écoute auto : jouer un échantillon à la sélection | PreviewPlayer.cs |
| Auto-audition: play on select | Pré-écoute auto : jouer à la sélection | PreviewPlayer.cs |
| Heard through {0} — click to pick a track, scroll to step through them | Entendu via {0} — cliquer pour choisir une piste, défiler pour les parcourir | PreviewPlayer.cs |
| Picked for this effect: drums for dynamics, keys for time and modulation, a full mix for tone | Choisi pour cet effet : batterie pour la dynamique, claviers pour le temps et la modulation, mix complet pour le timbre | PreviewPlayer.cs |
| Preview failed: {0} | Échec de la pré-écoute : {0} | PreviewPlayer.cs |
| Preview volume | Volume de pré-écoute | PreviewPlayer.cs |
| Preview volume {0} · ⌥-click to type · double-click for 0{1}dB | Volume de pré-écoute {0} · ⌥-clic pour saisir · double-clic pour 0{1}dB | PreviewPlayer.cs |
| Previewing {0} | Pré-écoute de {0} | PreviewPlayer.cs |
| Sample — pick one in Files | Échantillon — en choisir un dans Fichiers | PreviewPlayer.cs |
| Sample — {0} | Échantillon — {0} | PreviewPlayer.cs |
| Spec | Spec | PreviewPlayer.cs |
| Wave | Onde | PreviewPlayer.cs |
| no sound to preview | aucun son à pré-écouter | PreviewPlayer.cs |
| play to see the spectrum | lancer la lecture pour voir le spectre | PreviewPlayer.cs |
| {0}-bit float | {0} bits flottants | PreviewPlayer.cs |
| Balance | Balance | StereoMapView.cs |
| Correlation | Corrélation | StereoMapView.cs |
| Drag: pan + volume · Shift locks axis · double-click centres pan · ⌥-click types | Glisser : pan + volume · Shift verrouille l’axe · double-clic centre le pan · ⌥-clic pour saisir | StereoMapView.cs |
| Width | Largeur | StereoMapView.cs |
| Level | Niveau | StereoMapView.cs |
| Image is off the pan | L’image est décalée du pan | StereoMapView.cs |
| The pan is at {0}, but the signal measures {1}. The source itself is unbalanced. | Le pan est à {0}, mais le signal mesure {1}. La source elle-même est déséquilibrée. | StereoMapView.cs |
| Muted | En sourdine | StereoMapView.cs |
| No cloud while the track is muted. | Pas de nuage tant que la piste est en sourdine. | StereoMapView.cs |
| Below −60 dBFS, so the cloud is hidden. | Sous −60 dBFS : le nuage est masqué. | StereoMapView.cs |
| Silent | Silence | StereoMapView.cs |
| Arm for recording · ⌘-click to arm several tracks | Armer pour l’enregistrement · ⌘-clic pour armer plusieurs pistes | TrackArming.cs |
