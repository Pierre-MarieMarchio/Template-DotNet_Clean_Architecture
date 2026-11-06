namespace CleanArch.Create.Setup;

public sealed class Interruption
{
    private volatile bool _settingUp;
    private volatile bool _stopped;

    public bool Stopped => _stopped;

    public bool Interrupt()
    {
        _stopped = _settingUp;
        return _stopped;
    }

    public void WhileSettingUp(Action setUp)
    {
        _settingUp = true;
        try
        {
            setUp();
        }
        finally
        {
            _settingUp = false;
        }
    }
}
