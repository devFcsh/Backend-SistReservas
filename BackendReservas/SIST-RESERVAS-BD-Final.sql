CREATE DATABASE IF NOT EXISTS control_laboratorio;
USE control_laboratorio;

CREATE TABLE IF NOT EXISTS reservas (
    id INT AUTO_INCREMENT PRIMARY KEY,
    matricula VARCHAR(20) NOT NULL,
    nombre VARCHAR(100) NOT NULL,
    apellido VARCHAR(100) NOT NULL,
    carrera VARCHAR(100) NOT NULL,
    laboratorio VARCHAR(10) DEFAULT 'L002',
    hora_inicio TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    hora_salida TIMESTAMP NULL DEFAULT NULL,
    tiempo_promedio INT DEFAULT NULL -- Almacenará el tiempo en minutos
);

ALTER TABLE reservas ADD COLUMN equipo VARCHAR(50) NOT NULL AFTER laboratorio;




ALTER TABLE reservas MODIFY tiempo_promedio VARCHAR(20) DEFAULT NULL;

ALTER TABLE reservas ADD COLUMN periodo VARCHAR(10) NOT NULL;
ALTER TABLE reservas MODIFY COLUMN año INT NOT NULL;
SHOW COLUMNS FROM reservas LIKE 'año';

USE control_laboratorio;
SET SQL_SAFE_UPDATES = 0;
DELETE FROM reservas WHERE matricula = '202005088';
SET SQL_SAFE_UPDATES = 1;

USE control_laboratorio;
SELECT * FROM reservas;

SET SQL_SAFE_UPDATES = 1;


-- ==========================================================================
-- AUTENTICACIÓN DEL PERSONAL
-- El backend crea estas tablas automáticamente al arrancar si no existen
-- (Services/AuthInicializador.cs). Se dejan aquí como referencia.
-- ==========================================================================
USE control_laboratorio;

CREATE TABLE IF NOT EXISTS usuarios (
    id INT AUTO_INCREMENT PRIMARY KEY,
    usuario VARCHAR(50) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    rol VARCHAR(20) NOT NULL,              -- 'Admin' o 'Laboratorio'
    activo TINYINT(1) NOT NULL DEFAULT 1,
    creado_en DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS sesiones (
    id CHAR(36) PRIMARY KEY,               -- GUID; viaja dentro del token
    usuario_id INT NOT NULL,
    creada_en DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    cerrada_en DATETIME NULL,              -- NULL = sesión abierta
    INDEX idx_sesiones_usuario (usuario_id),
    CONSTRAINT fk_sesiones_usuario FOREIGN KEY (usuario_id)
        REFERENCES usuarios(id) ON DELETE CASCADE
);
