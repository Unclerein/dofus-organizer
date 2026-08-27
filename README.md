# Dofus Organizer

Un organizer multi-comptes pour Dofus Unity, dans l'esprit de celui de Naio : il détecte
les clients ouverts, permet de basculer de l'un à l'autre au clavier, et rejoue des
séquences de clics sur toute l'équipe — par exemple pour la soigner en une touche.

Windows uniquement. Aucune lecture de la mémoire du jeu, aucune interaction réseau :
l'outil ne fait que déplacer des fenêtres et rejouer vos propres clics.

## Récupérer l'exécutable

Chaque poussée sur le dépôt fait compiler un exécutable autonome par la CI :
onglet **Actions** → dernier run → artefact **DofusOrganizer-win-x64**. C'est un fichier
unique d'environ 63 Mo, à décompresser et lancer. Rien à installer, pas de runtime .NET
à ajouter.

Pour compiler soi-même, avec le SDK .NET 8 :

```
dotnet build
dotnet test
dotnet publish src/DofusOrganizer.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Mise en route

1. Lancez vos clients Dofus, puis Dofus Organizer. L'onglet **Personnages** liste ce qu'il
   a trouvé.
2. Un client pas encore entré en jeu apparaît avec la mention **« Écran de connexion »** et le
   titre brut de sa fenêtre. La touche « personnage suivant » y bascule comme sur les autres,
   ce qui sert justement à les connecter l'un après l'autre. La ligne s'efface d'elle-même dès
   que le personnage entre en jeu, et laisse la place à son emplacement nommé.
3. Si la liste reste sans aucun personnage nommé alors que la barre d'état annonce des clients,
   c'est le **motif du titre** dans les Réglages qui ne correspond pas à votre version. Il
   attend la forme « Nom - Classe - Version - Release ». Videz-le pour tout accepter tel quel,
   le temps de lire dans la colonne « Titre de la fenêtre » ce que l'application voit.
4. Si une fenêtre indésirable s'invite dans la liste, renseignez **Classe de fenêtre** dans les
   Réglages : `UnityWndClass` pour les clients Dofus Unity. L'infobulle de la colonne « Titre de
   la fenêtre » donne la classe de chaque ligne, de quoi vérifier avant de filtrer. Laissé vide,
   le champ n'écarte rien — c'est le réglage par défaut, parce que rater le jeu serait pire
   qu'en détecter un peu trop.
5. Réordonnez les personnages avec **Monter** / **Descendre** : cet ordre est celui que
   suivent la touche « personnage suivant » et les boucles de macro.
6. Sélectionnez un personnage, cliquez **Assigner**, appuyez sur la touche voulue.
7. Dans **Réglages**, assignez la touche « personnage suivant ».

Un personnage dont le client est fermé **reste dans la liste**, grisé : c'est ce qui garde son
raccourci et sa position d'une session à l'autre. **Oublier les absents** les retire tous d'un
coup quand la liste s'est encombrée — leurs raccourcis avec, d'où le fait que ce ne soit pas
automatique.

Un client qui n'a pas encore de personnage en jeu — au chargement, ou à l'écran de sélection —
tient une ligne marquée **« Écran de connexion »**, en fin de liste. La touche « personnage
suivant » y bascule comme sur les autres, ce qui est commode pour les connecter l'un après
l'autre. Cette ligne-là ne retient rien : ni raccourci, ni place dans l'ordre, et elle
s'efface d'elle-même dès que le personnage entre en jeu, sans laisser de doublon derrière
elle. Le titre d'un client change deux fois avant de nommer un personnage, et c'est
précisément d'avoir voulu retenir quelque chose sous un titre de passage que naissaient les
doublons. Les macros, elles, ignorent ces lignes : rejouer une séquence de sorts sur un écran
de sélection n'aurait pas de sens.

**Jouez en mode fenêtré ou fenêtré sans bordure.** En plein écran exclusif, Windows gère
mal le passage d'une fenêtre à l'autre et les changements sont lents ou refusés.

## Écrire une macro de soin

L'idée : enregistrer la séquence sur **un seul** personnage, et laisser la boucle la
rejouer sur tous.

1. Dans **Réglages**, assignez une touche à « Démarrer / arrêter l'enregistrement ». Elle
   reste active depuis le jeu, y compris pendant une capture : vous n'aurez pas à revenir
   dans cette fenêtre, ce qui polluerait l'enregistrement.
2. Onglet **Macros**, sélectionnez « Soin de l'équipe » (ou créez-en une et ajoutez une
   étape « Pour chaque personnage »). Les temps morts ne sont pas enregistrés par défaut ;
   la case est dans les Réglages si vous en voulez.
3. Basculez sur un personnage, appuyez sur la touche d'enregistrement (un bip confirme),
   cliquez sur le sort de soin puis sur la cible, et appuyez de nouveau pour arrêter
   (deux bips).
4. Les actions capturées sont placées d'office dans la boucle « pour chaque personnage »
   si la macro en contient une.
5. Cliquez **Assigner** pour donner une touche à la macro, puis **Tester**.

Pendant la capture, la barre d'état affiche chaque action au fur et à mesure
(« Clic gauche à 42,3 % / 91,2 % ») : de quoi repérer tout de suite une position aberrante.
Seuls les clics faits dans un client Dofus sont retenus — l'organizer lui-même et les autres
applications sont ignorés.

Les positions sont enregistrées en fraction de la fenêtre et non en pixels de l'écran :
la macro reste juste si vous déplacez ou redimensionnez vos clients ensuite. Elle suppose en
revanche que l'interface soit dans le même état chez tout le monde — c'est à vous d'y veiller,
l'outil rejoue des positions, il ne regarde pas l'écran.

## Téléporter toute l'équipe au même zaap

Le problème : il y a des centaines de zaaps, donc écrire une macro par destination est
impossible. La réponse est **« Refaire sur l'équipe »** — l'outil n'a pas besoin de savoir de
quel zaap il s'agit, il refait simplement ce que vous venez de faire.

1. Dans **Réglages**, assignez une touche à « Refaire sur l'équipe ».
2. **Triez la liste des zaaps à l'identique sur tous vos personnages** (par ordre alphabétique
   par exemple). C'est la condition qui rend l'ensemble fiable.
3. Sur votre meneur, appuyez sur la touche (un bip), prenez le zaap normalement, appuyez de
   nouveau (deux bips). Les autres personnages refont l'enchaînement — la barre d'état annonce
   chaque personnage visité.

La séquence capturée est conservée dans l'onglet **Macros** sous le nom « Refaire sur l'équipe
(dernière capture) », remplacée à chaque usage. C'est là qu'il faut aller quand le rejeu déçoit :
on y lit l'enchaînement obtenu, on corrige une étape, et on relance avec **Tester**.

### Revenir aux réglages d'origine

Deux boutons, chacun au pied de la section qu'il concerne dans l'onglet Réglages :

- **Rétablir les temporisations** remet les six valeurs de la section Temporisations à celles
  d'une installation neuve. Pas les touches lentes : ce sont bien des temporisations, mais
  chacune est attachée à une touche choisie à la main — celle du havre-sac, typiquement — et
  les effacer casserait une téléportation sans rien dire. Elles se retirent une à une avec
  **Supprimer**, juste en dessous.
- **Rétablir la détection** remet le motif du titre à sa valeur d'origine et vide le filtre de
  classe. C'est la porte de sortie quand un motif saisi à la main ne reconnaît plus rien : il
  n'y a pas à retrouver l'expression régulière d'origine pour la retaper.

Deux boutons et non un seul parce que les deux pannes n'arrivent pas ensemble : réparer un
motif de titre ne doit pas faire perdre des temporisations réglées machine par machine.

Ni l'un ni l'autre ne touche aux raccourcis — ils sont pourtant rangés dans le même objet que
les temporisations, d'où des tests qui vérifient nommément qu'ils survivent — ni aux cases du
comportement, ni aux macros et aux personnages.

### Quand une touche met du temps à agir

Un voyage commence souvent par la touche du havre-sac, dont le panneau met parfois un moment à
s'ouvrir — et le clic suivant part alors dans le vide. Monter les délais généraux couvrirait le
cas, mais ralentirait toute la séquence, sur chaque personnage.

**Réglages → Touches lentes → Ajouter**, assignez la touche et son attente supplémentaire
(1500 ms pour commencer, à baisser tant que ça passe). Cette attente s'ajoute à la pause
ordinaire et vaut pour toutes les macros.

Elle porte sur la **touche** et non sur une étape, et c'est délibéré : la séquence de « Refaire
sur l'équipe » est recapturée à chaque voyage, donc une étape d'attente ajoutée à la main dans
ses étapes serait perdue au voyage suivant. « Cette touche ouvre un panneau lent » est une
propriété du jeu, pas d'une capture — exprimée ainsi, le réglage survit à toutes les recaptures.

Sachez ce que vous achetez : une attente fixe doit être taillée pour le pire cas, subie même les
fois où l'ouverture est immédiate, et multipliée par le nombre de personnages — 1,5 s sur huit
clients, c'est douze secondes ajoutées au voyage.

**Passez par la liste des zaaps, pas par la carte du monde.** La carte peut avoir un zoom
différent selon le personnage, et un même zaap ne s'y trouve alors pas au même endroit ; une
liste de texte, elle, s'affiche identiquement partout.

## Aligner un panneau identiquement sur tous les personnages

La fenêtre listant les zaaps se déplace au clic gauche maintenu. Pour qu'elle soit au même
endroit chez tout le monde — condition pour que les positions de clic se correspondent —
enregistrez ce déplacement une fois :

1. Ouvrez le panneau sur votre meneur, lancez la capture, **faites glisser le panneau à la main**
   jusqu'à sa place, arrêtez la capture.
2. L'étape apparaît comme « Glisser depuis … jusqu'à … ».
3. Rejouez sur l'équipe : chaque personnage saisit son panneau au même endroit et le dépose à la
   même position.

C'est un panneau dessiné par le jeu, pas une fenêtre du système : Windows ne peut pas le
déplacer, seul le glisser-déposer le peut. Les deux points étant fixes, cela suppose que le
panneau se soit ouvert au même endroit chez chacun — c'est le cas quand ils sont restés alignés
depuis la dernière fois.

### À savoir sur le rejeu

- Un **double-clic** est reconnu comme tel à la capture et rejoué comme un seul geste. L'étape
  s'affiche alors « ×2 » ; le champ **Répétitions** permet aussi de le régler à la main.

  Les deux clics sont espacés de 80 ms, réglable par « Entre deux clics d'un double-clic » dans
  les Réglages. Cet écart obéit à deux bornes : trop court, les deux clics tombent dans la même
  image du jeu, qui n'interroge l'entrée qu'une fois par image et n'en voit alors qu'un ; trop
  long, il dépasse le seuil du système — une demi-seconde en général — et le jeu voit deux clics
  indépendants. **Si un double-clic n'aboutit pas alors que l'étape affiche bien « ×2 », c'est ce
  réglage qu'il faut monter**, à 120 puis 150 ms. Au-delà de 400 ms c'est inutile.

- La **molette** : les crans d'un même geste sont regroupés en une seule étape à la capture, et
  repartent en une seule injection — sans aucun écart entre eux, comme le geste d'origine. Sans
  ce regroupement, parcourir une liste donnerait dix ou vingt étapes que le rejeu espacerait
  chacune de son délai. Changer de sens ou marquer une pause ouvre un nouveau geste.

  Le délai qui suit un défilement est court et distinct des autres — « Après un cran de molette »
  dans les Réglages — parce qu'une liste défile à la vitesse où on la fait tourner, sans rien à
  ouvrir ni à charger.
- **Une saisie** — taper le nom d'un zaap dans un champ de recherche — est traitée de même : des
  touches qui s'enchaînent sont espacées de « Entre deux touches enchaînées » et non du délai des
  actions. Sans quoi écrire « Bonta » coûterait une demi-seconde par lettre lors d'un rejeu sur
  l'équipe, multipliée par le nombre de personnages. Seule la **dernière** touche d'un mot garde le
  délai ordinaire : c'est après elle que le jeu a quelque chose à faire.

Si des clics se perdent, augmentez « Après un changement de fenêtre » dans les Réglages —
le client a besoin de quelques images avant d'accepter une entrée.

## Arrêt d'urgence

La touche **Pause** (modifiable) interrompt immédiatement la macro en cours. Elle reste
active même quand Dofus n'a pas le focus : c'est le moyen de reprendre la main si une
macro part de travers.

## Si rien ne se passe

- **Aucun raccourci ne répond** : un client lancé en administrateur est invisible pour un
  organizer qui ne l'est pas. L'application le détecte et l'affiche dans la barre d'état ;
  relancez l'un ou l'autre au même niveau de privilèges.
- **Les touches n'arrivent pas dans le jeu** : décochez « Envoyer les touches avec leur
  code de balayage » dans les Réglages. Les moteurs de jeu se partagent entre les deux
  façons de lire le clavier.
- **Un seul raccourci ne répond plus, les autres marchent** : sa touche a été considérée
  comme restée enfoncée — son relâchement s'est perdu — et l'organizer la prenait pour une
  répétition automatique. Il s'en aperçoit désormais tout seul en moins d'une seconde et le
  signale dans la barre d'état ; il n'y a plus à redémarrer.
- **Plus aucun raccourci ne répond après un moment d'utilisation** : Windows retire la
  surveillance du clavier dès qu'elle tarde à rendre la main, et il le fait sans rien signaler.
  L'organizer le détecte maintenant en comparant ce qu'il a vu à ce que le système a reçu, et la
  repose en deux ou trois secondes — la barre d'état l'annonce. S'il n'y arrive pas, elle le dit
  aussi : un autre logiciel bloque probablement la pose, et là il faut relancer.
- **Les clics tombent à côté** : vérifiez que l'écran concerné n'a pas changé de mise à
  l'échelle depuis l'enregistrement, et réenregistrez la séquence au besoin.

- **L'application ne s'ouvre pas du tout** : le détail de l'erreur, chaîne d'exceptions
  et pile d'appels comprises, est écrit dans `%APPDATA%\DofusOrganizer\crash.log`. C'est
  ce fichier qu'il faut regarder, pas le message de la boîte de dialogue.

La configuration est un simple fichier JSON, lisible et modifiable à la main :
`%APPDATA%\DofusOrganizer\profile.json`.

## Ce que fait — et ne fait pas — cet outil

Les macros sont **déclenchées par vous**, se déroulent une fois et s'arrêtent. Il n'y a ni
boucle automatique, ni lecture de l'écran, ni prise de décision : l'outil rejoue une séquence
de clics enregistrée aux positions où vous les avez faits, rien de plus.

Cela reste de l'automatisation d'entrées, et les conditions d'utilisation d'Ankama
encadrent ce genre d'usage. Un organizer multi-comptes est d'un usage courant ; un
programme qui jouerait à votre place sans supervision est ce qui fait sanctionner un
compte. Le mode retenu ici est le premier, à vous de le garder de ce côté.

## Organisation du code

| Projet | Rôle |
|---|---|
| `src/DofusOrganizer.Core` | Modèles, moteur de macro, conversions de coordonnées, persistance. Sans dépendance à Windows, donc testable partout. |
| `src/DofusOrganizer.Windows` | Appels Win32 : détection et activation des fenêtres, `SendInput`, hooks clavier/souris, enregistreur. |
| `src/DofusOrganizer.App` | Interface WPF. |
| `tests/DofusOrganizer.Core.Tests` | Tests de la logique métier, exécutés en CI sur Linux. |
| `assets/` + `tools/build-icon.py` | Illustration de l'icône et fabrication du `.ico` posé sur l'exécutable. |

Cette séparation est délibérée : tout ce qui peut se tromper silencieusement — le calcul
des coordonnées de clic, l'ordre de parcours des personnages, la sérialisation des macros —
vit dans `Core` derrière des interfaces, et se vérifie sans Windows ni client Dofus.

L'icône de l'exécutable est fabriquée par `python3 tools/build-icon.py` (nécessite Pillow),
qui recadre `assets/icon.*` sur le dessin puis en tire les neuf tailles que Windows réclame,
de 16 à 256 pixels. Elle est versionnée : le script n'est à relancer qu'après un changement
de dessin. Une icône Windows n'étant pas une image mais un conteneur de plusieurs tailles,
la produire par un script plutôt qu'à la main garde le résultat reproductible.

Le script encaisse une illustration livrée sur fond uni, sans couche alpha, qu'il détoure —
sans quoi l'icône serait un carré blanc sur une barre des tâches sombre. Et il réduit en
alpha prémultiplié : réduire une image transparente telle quelle mélange la couleur des
pixels invisibles à celle de leurs voisins, ce qui ramène le fond en halo clair sur le
pourtour, d'autant plus visible que la taille est petite.

Reste l'interface, que les tests ne couvrent pas. `DofusOrganizer.exe --selftest` construit
la fenêtre principale, parcourt les commandes d'édition puis calcule la mise en page de
chaque onglet, et rend un code de sortie. La mise en page compte autant que le reste :
charger le XAML ne fait que le lire, alors qu'une ressource mal typée dans un
`ControlTemplate` ne se révèle qu'à l'application du gabarit, c'est-à-dire à la première
mesure — et le contenu d'un onglet jamais sélectionné n'est jamais construit. La CI lance
ce contrôle sur `windows-latest` après la compilation.
