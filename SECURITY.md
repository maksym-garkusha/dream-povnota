# Безпека

dream-povnota це незалежний навчальний проєкт на відкритих даних DREAM, не пов'язаний з державними органами. Якщо ви знайшли вразливість, повідомте приватно, а не у відкритому issue.

## Як повідомити

На вкладці Security цього репозиторію натисніть «Report a vulnerability». Це приватне повідомлення GitHub, його бачить тільки власник репозиторію. Опишіть, що знайшли і як це відтворити. Запит і відповідь, якщо вони є, додавайте без чужих персональних даних.

Відповідь протягом 7 днів. Винагород за знахідки немає.

## Що в межах

- сторінка https://maksym-garkusha.github.io/dream-povnota/ і її `data.json`;
- веб-сервіс https://dream-povnota.onrender.com: сторінки, API `/api/v1`, вивантаження, зрізи для служб відновлення;
- Telegram-бот @dream_povnota_bot. Іншого бота в проєкту немає: бот зі схожим ім'ям, який пише від імені dream-povnota, до проєкту не належить;
- код і workflow GitHub Actions у цьому репозиторії.

## Що поза межами

- сама система DREAM і її API: про них варто писати власнику системи;
- GitHub, Render і Telegram як платформи;
- навантажувальні тести і перебір: у сервісу безкоштовний екземпляр з 0,1 CPU, і такий тест зупинить його для всіх;
- соціальна інженерія.

## English

dream-povnota is an independent study project on open DREAM data, not affiliated with any government body. To report a vulnerability, use "Report a vulnerability" on the Security tab of this repository (GitHub private vulnerability reporting) rather than a public issue.

In scope: the page on GitHub Pages and its `data.json`, the service at https://dream-povnota.onrender.com, the Telegram bot @dream_povnota_bot (the project has no other bot) and the code and workflows in this repository. Out of scope: DREAM itself, the hosting platforms, load testing and social engineering.

You will get a reply within 7 days. There is no bug bounty.
