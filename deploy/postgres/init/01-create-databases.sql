-- Runs once, when the PostgreSQL volume is created.
-- merp_catalog: tenant catalog + Hangfire (D3). merp_demo: database of the "demo" tenant.
CREATE DATABASE merp_catalog;
CREATE DATABASE merp_demo;

\connect merp_demo
CREATE EXTENSION IF NOT EXISTS ltree;
