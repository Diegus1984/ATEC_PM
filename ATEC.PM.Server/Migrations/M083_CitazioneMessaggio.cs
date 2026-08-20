using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v83 — Chat stile WhatsApp Wave 1: citazione (reply_to) sui messaggi.
public sealed class M083_CitazioneMessaggio : IMigrazione
{
    public int Versione => 83;

    public string Descrizione => "project_chat_messages.reply_to_message_id: citazione messaggio";

    public void Applica(MySqlConnection c, ILogger log)
    {
        try
        {
            c.Execute(@"ALTER TABLE project_chat_messages
                ADD COLUMN reply_to_message_id INT NULL AFTER attachment_path");
        }
        catch { /* colonna già presente */ }

        log.LogInformation("[Migration v83] Chat: colonna reply_to_message_id.");
    }
}
