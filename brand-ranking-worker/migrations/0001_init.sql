CREATE TABLE brands(id TEXT PRIMARY KEY, name TEXT NOT NULL);
INSERT INTO brands VALUES ('lenovo','联想'),('asus','华硕'),('hp','惠普'),('dell','戴尔'),('apple','苹果'),('acer','宏碁'),('msi','微星'),('huawei','华为'),('honor','荣耀'),('xiaomi','小米'),('mechrevo','机械革命'),('hasee','神舟'),('gigabyte','技嘉'),('razer','雷蛇'),('microsoft','微软'),('samsung','三星'),('lg','LG'),('thunderobot','雷神'),('machenike','机械师'),('other','其他品牌');
CREATE TABLE reviews(id TEXT PRIMARY KEY, brandId TEXT NOT NULL REFERENCES brands(id), device TEXT NOT NULL, nickname TEXT NOT NULL, model TEXT NOT NULL, content TEXT NOT NULL, score INTEGER NOT NULL CHECK(score BETWEEN 1 AND 10), createdAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')), UNIQUE(brandId,device));
CREATE INDEX reviews_brand_date ON reviews(brandId,createdAt DESC);
CREATE TABLE throttle(ip TEXT PRIMARY KEY, touched INTEGER NOT NULL);
