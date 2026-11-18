-- Ключі ASP.NET Data Protection, якими підписано токени форм. Безкоштовний екземпляр
-- засинає, і без цієї таблиці кожне пробудження давало б новий набір ключів,
-- а відкрита до сну форма перевірки відповідала б «Сторінка застаріла».
create table data_protection_key (
  name       text primary key,
  xml        text not null,
  created_at timestamptz not null default now()
);
