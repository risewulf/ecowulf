# EcoWulf

**EcoWulf** est un calculateur de prix auto-hébergé pour le jeu [ECO](https://play.eco/),
maintenu par la communauté **« À la bonne heure ! »**. Il aide les joueurs à déterminer les
prix d'achat et de vente optimaux de leurs objets en fonction de leurs compétences, tables
d'artisanat, talents et recettes.

## 🙏 Crédits — basé sur Eco Gnome

EcoWulf est une **version rebrandée et auto-hébergée** de l'excellent projet open-source
**[Eco Gnome](https://github.com/Eco-Gnome/eco-gnome-website)**, créé par **Zangdar** et
**Joridan**. Tout le mérite du moteur de calcul et de l'outil original leur revient.
Ce dépôt reprend leur travail sous licence MIT, avec des modifications de marque, des pages
légales, un thème et une configuration d'auto-hébergement.

- Projet original : https://github.com/Eco-Gnome/eco-gnome-website
- Mod serveur (export des données) : https://github.com/Eco-Gnome/eco-gnome-mod

## Modifications apportées dans EcoWulf

- Rebranding « Eco Gnome » → « EcoWulf » (nom, logo SVG, thème vert, méta/SEO).
- Pages **Politique de confidentialité**, **Conditions d'utilisation** et **Mentions légales**.
- Page **Contact** adaptée à la communauté « À la bonne heure ! ».
- Configuration d'auto-hébergement (Docker Compose : Postgres + app).

Les références au mod en jeu (`/EcoGnome`, `EcoGnomeMod`) et les crédits aux auteurs
originaux sont **conservés** intentionnellement.

## Tech

C# avec Blazor (Server) et la librairie front MudBlazor. Base de données PostgreSQL.

## Auto-hébergement (Docker)

```bash
# 1) Renseigner un mot de passe Postgres
cp deploy/prod/.env.example .env   # puis éditer POSTGRES_PASSWORD

# 2) Construire et lancer (depuis la racine du dépôt)
docker compose build app
docker compose up -d db app
```

L'app écoute sur le port `8080` (à placer derrière un reverse proxy type nginx + HTTPS).

## Développement

Avec Rider (JetBrains), les dépendances se téléchargent automatiquement.
Certificat HTTPS de dev :
```
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```

## Licence

MIT — voir [LICENSE](LICENSE). Crédits au projet original **Eco Gnome** (Zangdar, Joridan).
