import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../api.dart';
import 'common.dart';

class AttendanceScreen extends StatefulWidget {
  const AttendanceScreen({super.key});

  @override
  State<AttendanceScreen> createState() => _AttendanceScreenState();
}

class _AttendanceScreenState extends State<AttendanceScreen> {
  Map<String, dynamic>? _me;
  bool _busy = false;
  static final _fmt = DateFormat('dd MMM, hh:mm a');

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final r = await Api.instance.get('pos/me/attendance') as Map<String, dynamic>;
      if (mounted) setState(() => _me = r);
    } catch (e) {
      if (mounted) showError(context, e);
    }
  }

  Future<void> _clock(bool into) async {
    setState(() => _busy = true);
    try {
      await Api.instance.post('pos/me/attendance/${into ? 'clock-in' : 'clock-out'}');
      await _load();
      if (mounted) showInfo(context, into ? 'Clocked in.' : 'Clocked out.');
    } catch (e) {
      if (mounted) showError(context, e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  String _t(String? iso) => iso == null ? '—' : _fmt.format(DateTime.parse(iso).toLocal());

  @override
  Widget build(BuildContext context) {
    final me = _me;
    return Scaffold(
      appBar: AppBar(title: const Text('Attendance')),
      body: me == null
          ? const Busy()
          : me['isEmployee'] != true
          ? const Center(child: Text('Your account is not linked to an employee record.'))
          : ListView(
              padding: const EdgeInsets.all(16),
              children: [
                Text(
                  me['isClockedIn'] == true ? 'Clocked in since ${_t((me['open'] as Map)['clockInAt'] as String)}' : 'Not clocked in',
                  style: const TextStyle(fontSize: 18),
                ),
                if (me['assignedBranchName'] != null) Text('Branch: ${me['assignedBranchName']}'),
                const SizedBox(height: 16),
                FilledButton(
                  onPressed: _busy ? null : () => _clock(me['isClockedIn'] != true),
                  child: Padding(padding: const EdgeInsets.all(12), child: Text(me['isClockedIn'] == true ? 'Clock out' : 'Clock in')),
                ),
                const Divider(height: 32),
                for (final a in (me['recent'] as List).cast<Map<String, dynamic>>())
                  ListTile(
                    title: Text('${_t(a['clockInAt'] as String)} → ${_t(a['clockOutAt'] as String?)}'),
                    subtitle: Text('${a['branchName']}${a['hoursWorked'] != null ? ' · ${(a['hoursWorked'] as num).toStringAsFixed(1)} h' : ''}'),
                  ),
              ],
            ),
    );
  }
}
