-- Permisos del rol de la aplicación sobre lo que existe **ahora mismo**.
--
-- Se ejecuta **después de cada migración**, y hay que acordarse: las tablas las crea el dueño, así que
-- una tabla nueva nace sin permisos para `matchketing_app` y la aplicación se cae con «permission
-- denied» en la primera petición que la toque. Es idempotente y tarda milisegundos, así que va en el
-- despliegue al lado de las migraciones y no como paso manual.
--
-- Los esquemas se recorren **de la base, no de una lista escrita aquí**. Una lista que hay que acordarse
-- de ampliar es una lista que se queda corta con el módulo siguiente; eso ya pasó en el guion de
-- comprobación del aislamiento y se arregló igual.
DO $$
DECLARE
    esquema text;
    -- El rol se puede cambiar con `SET mk.rol = '…'` antes de ejecutar esto. En producción no se toca
    -- y vale `matchketing_app`; lo usan las pruebas de integración, que crean su propio rol y no
    -- pueden ir cambiándole la contraseña al de un despliegue que viva en el mismo servidor.
    rol text := coalesce(nullif(current_setting('mk.rol', true), ''), 'matchketing_app');
BEGIN
    FOR esquema IN
        SELECT nspname FROM pg_namespace
        WHERE nspname NOT LIKE 'pg\_%'
          AND nspname NOT IN ('information_schema', 'public')
    LOOP
        EXECUTE format('GRANT USAGE ON SCHEMA %I TO %I', esquema, rol);

        -- Las cuatro operaciones de datos y ninguna de estructura: no puede crear, alterar ni borrar
        -- una tabla, ni saltarse una política. Puede leer y escribir filas, que es su trabajo.
        EXECUTE format(
            'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO %I', esquema, rol);
        EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO %I', esquema, rol);
        EXECUTE format('REVOKE CREATE ON SCHEMA %I FROM %I', esquema, rol);

        -- Y para lo que cree el dueño a partir de ahora en ese esquema, sin tener que volver a pasar
        -- por aquí. No sustituye a ejecutar este guion tras cada migración —un esquema **nuevo** no
        -- existe todavía cuando esto corre—, pero cubre la tabla añadida a un esquema que ya estaba.
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES IN SCHEMA %I '
            || 'GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I', esquema, rol);
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT USAGE, SELECT ON SEQUENCES TO %I',
            esquema, rol);
    END LOOP;
END
$$;

-- La tabla de historial de EF: la aplicación **no** la necesita, y sin permiso encima se nota antes si
-- alguien intenta migrar desde el proceso que atiende peticiones.
DO $$
DECLARE rol text := coalesce(nullif(current_setting('mk.rol', true), ''), 'matchketing_app');
BEGIN
    EXECUTE format('REVOKE ALL ON TABLE public."__EFMigrationsHistory" FROM %I', rol);
END
$$;
