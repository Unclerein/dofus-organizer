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
2. Si la liste est vide, allez dans **Réglages** : la colonne « Titre de la fenêtre » de
   l'onglet Personnages montre ce que l'application voit, ce qui permet d'ajuster le motif
   d'extraction des noms ou la classe de fenêtre.
3. Réordonnez les personnages avec **Monter** / **Descendre** : cet ordre est celui que
   suivent la touche « personnage suivant » et les boucles de macro.
4. Sélectionnez un personnage, cliquez **Assigner**, appuyez sur la touche voulue.
5. Dans **Réglages**, assignez la touche « personnage suivant ».

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
la macro reste juste si vous déplacez ou redimensionnez vos clients ensuite. Chaque clic
emporte en plus l'image de ce qu'il visait — au rejeu, l'outil cherche cette image et clique
là où il la trouve, la position ne servant qu'à délimiter la recherche.

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
on y voit les images capturées et l'enchaînement obtenu, on peut corriger une étape ou un seuil,
et relancer avec **Tester**.

**Passez par la liste des zaaps, pas par la carte du monde.** La carte peut avoir un zoom
différent selon le personnage, et un même zaap ne s'y trouve alors pas au même endroit ; une
liste de texte, elle, s'affiche identiquement partout. La reconnaissance d'image ne rattrape
pas un changement d'échelle.

## Enchaîner un dialogue de PNJ

Même principe, avec une macro cette fois : clic sur le PNJ, **attente sur image** jusqu'à ce
que le panneau apparaisse, clic sur la réponse, attente, fermeture. L'attente sur image
remplace les délais devinés — l'ouverture du panneau devient un fait constaté et non un pari
sur 300 ms.

Cela vaut pour un PNJ dont l'enchaînement est toujours le même. Un dialogue dont les réponses
varient sort de ce que l'outil sait faire : il ne lit pas le texte, il reconnaît des images.

### À savoir sur la reconnaissance

- L'image capturée est **large et courte** (160 × 48 px par défaut), à la forme d'une ligne
  d'interface : un fragment carré et étroit peut ne contenir que quelques caractères — voire du
  fond vide si le clic tombe après la fin d'un libellé court — et ressemble alors à toutes les
  autres lignes. C'est la première chose à vérifier dans l'aperçu si la mauvaise ligne est
  choisie.
- La ressemblance exigée est volontairement tolérante (85 %), le rendu n'étant jamais identique
  au pixel près d'une image à l'autre. Si une image n'est pas retrouvée, baissez ce seuil dans
  l'éditeur d'étape ; si c'est la mauvaise qui est trouvée, élargissez plutôt l'image avant de
  monter le seuil.
- **Une image introuvable ne bloque pas la macro** : l'étape retombe sur sa position
  enregistrée et le signale dans la barre d'état. Le rejeu continue.
- L'aperçu affiché dans l'éditeur montre exactement ce qui a été capturé : c'est le premier
  endroit à regarder quand une étape se comporte mal.

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
- **Les clics tombent à côté** : vérifiez que l'écran concerné n'a pas changé de mise à
  l'échelle depuis l'enregistrement, et réenregistrez la séquence au besoin.

- **L'application ne s'ouvre pas du tout** : le détail de l'erreur, chaîne d'exceptions
  et pile d'appels comprises, est écrit dans `%APPDATA%\DofusOrganizer\crash.log`. C'est
  ce fichier qu'il faut regarder, pas le message de la boîte de dialogue.

La configuration est un simple fichier JSON, lisible et modifiable à la main :
`%APPDATA%\DofusOrganizer\profile.json`.

## Ce que fait — et ne fait pas — cet outil

Les macros sont **déclenchées par vous**, se déroulent une fois et s'arrêtent. Il n'y a ni
boucle automatique, ni lecture de l'état du jeu, ni prise de décision : l'outil rejoue une
séquence de clics enregistrée, rien de plus.

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

Cette séparation est délibérée : tout ce qui peut se tromper silencieusement — le calcul
des coordonnées de clic, l'ordre de parcours des personnages, la sérialisation des macros —
vit dans `Core` derrière des interfaces, et se vérifie sans Windows ni client Dofus.
